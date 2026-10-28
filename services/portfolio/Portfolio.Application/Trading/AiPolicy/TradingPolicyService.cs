using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingPolicyService(
    ITradingStore store,
    ITradingPolicyCandidateReader candidates,
    ITradingPolicyEvaluator evaluation,
    IReadOnlyOrderStore reviewedOrders,
    IAccountContext account,
    SemaphoreSlim gate,
    TimeProvider time
)
{
    public async Task Review(CancellationToken cancellationToken)
    {
        var initial = await store.Read(cancellationToken);
        var now = time.GetUtcNow();
        if (
            !Eligible(initial, account.AccountKey, now)
            || initial.AiPolicy?.LastCheckedAt is { } checkedAt && now - checkedAt < TimeSpan.FromMinutes(15)
        )
        {
            return;
        }

        // Persist a review admission before network work. Restart does not immediately
        // repeat an expensive or partially completed review.
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await store.Read(cancellationToken);
            if (current.Revision != initial.Revision || current.AiPolicy is not { } authority)
            {
                return;
            }

            initial = current with
            {
                Revision = current.Revision + 1,
                AiPolicy = authority with
                {
                    LastCheckedAt = now,
                    Status = "reviewing",
                    Reason = "Checking the team's reviewed candidate.",
                },
            };
            if (!await store.Save(initial, current.Revision, cancellationToken))
            {
                return;
            }
        }
        finally
        {
            gate.Release();
        }

        TradingPolicyCandidate? candidate = null;
        TradingPolicyEvaluation? result = null;
        TradingRuleSet? proposed = null;
        var reason = "Waiting for a fresh reviewer-approved candidate; current settings continue.";
        try
        {
            candidate = await candidates.Read(cancellationToken);
            if (candidate is not null)
            {
                candidate.Validate(time.GetUtcNow());
                if (candidate.AccountKey != account.AccountKey || candidate.BaselineRulesHash != initial.RulesHash)
                {
                    throw new InvalidDataException("Candidate account or baseline changed.");
                }

                if (
                    initial.PolicyChanges.Any(change => change.Candidate.RunId == candidate.RunId)
                    || initial.PolicyReview?.Candidate.RunId == candidate.RunId
                )
                {
                    candidate = null;
                    reason = "This research candidate was already reviewed; waiting for new evidence.";
                }
                else if (!Flat(initial) || await this.ManualOrders(cancellationToken))
                {
                    reason = "Settings will be reviewed after existing orders and positions are resolved.";
                }
                else
                {
                    proposed = candidate.Parameters.Apply(initial.Rules);
                    if (TradingRuleHash.Compute(proposed) == initial.RulesHash)
                    {
                        reason = "The research team retained the current settings.";
                    }
                    else
                    {
                        result = await evaluation.Evaluate(initial, proposed, cancellationToken);
                        reason = result.Reason;
                    }
                }
            }
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or InvalidDataException
                        or HttpRequestException
                        or System.Text.Json.JsonException
                || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested
            )
        {
            reason = "The policy review is unavailable or invalid. Existing settings continue; no change was applied.";
            result = null;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await store.Read(cancellationToken);
            now = time.GetUtcNow();
            if (
                !Eligible(current, account.AccountKey, now)
                || current.RulesHash != initial.RulesHash
                || current.AiPolicy is not { } authority
                || authority.MandateId != initial.AiPolicy?.MandateId
            )
            {
                return;
            }

            var next = current with { AiPolicy = authority with { Status = "waiting", Reason = reason } };
            if (candidate is not null && result is not null)
            {
                next = next with { PolicyReview = new(candidate, result) };
            }

            var manualExposure = result is { Accepted: true } && await this.ManualOrders(cancellationToken);
            now = time.GetUtcNow();

            if (
                candidate is not null
                && proposed is not null
                && result is { Accepted: true }
                && Eligible(current, account.AccountKey, now)
                && result.BaselineRulesHash == current.RulesHash
                && result.CandidateRulesHash == TradingRuleHash.Compute(proposed)
                && candidate.ExpiresAt > now
                && candidate.AsOf <= now
                && now - candidate.AsOf <= TimeSpan.FromMinutes(30)
                && result.ObservedAt <= now
                && now - result.ObservedAt <= TimeSpan.FromSeconds(proposed.MaxDataAgeSeconds)
                && Flat(current)
                && !manualExposure
                && current.Autonomy?.Capital is { HasUncertainOrders: false, Blocker: null } capital
                && capital.AccountKey == account.AccountKey
                && capital.ObservedAt <= now
                && now - capital.ObservedAt <= TimeSpan.FromSeconds(proposed.MaxDataAgeSeconds)
                && initial.Autonomy?.Capital is { } evaluatedCapital
                && capital.SpendableThb == evaluatedCapital.SpendableThb
                && capital.EquityThb == evaluatedCapital.EquityThb
                && capital.ExposureThb == evaluatedCapital.ExposureThb
                && capital.ReservedThb == evaluatedCapital.ReservedThb
            )
            {
                var hash = TradingRuleHash.Compute(proposed);
                next = next with
                {
                    Rules = proposed,
                    RulesHash = hash,
                    AiPolicy = authority with
                    {
                        EffectiveRulesHash = hash,
                        Status = "applied",
                        Reason = candidate.Reason,
                    },
                    PolicyChanges = current
                        .PolicyChanges.Append(new(authority, candidate, current.Rules, proposed, result, now))
                        .ToArray(),
                    Evaluation = null,
                    // Old market decisions and cached capital were scoped to the old hash.
                    Autonomy = null,
                };
            }
            else if (result is { Accepted: true })
            {
                next = next with
                {
                    AiPolicy = authority with
                    {
                        Status = "waiting",
                        Reason = "Account, authority or evidence changed during review; current settings continue.",
                    },
                };
            }

            await store.Save(next with { Revision = current.Revision + 1 }, current.Revision, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> ManualOrders(CancellationToken cancellationToken) =>
        (await reviewedOrders.Exposure(time.GetUtcNow(), cancellationToken)).Any(order =>
            order.State
                is OrderState.Submitting
                    or OrderState.Unknown
                    or OrderState.Submitted
                    or OrderState.PartiallyFilled
            || order.Sizing is not null
        );

    private static bool Eligible(TradingState state, string accountKey, DateTimeOffset now) =>
        state.AiPolicy is not null
        && TradingAuthority.Valid(state, accountKey, now)
        && state.Fault is null
        && !TradingCashBaselineAccounting.CashUnknown(state.Live);

    private static bool Flat(TradingState state) =>
        !state.Paper.Positions.Concat(state.Live.Positions).Any(position => position.Quantity > 0)
        && !state.Paper.Orders.Concat(state.Live.Orders).Any(TradingLedgerAccounting.Active);
}
