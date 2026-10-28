using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingPolicyChecks
{
    private static readonly JsonSerializerOptions legacyOptions = new()
    {
        TypeInfoResolver = TradingJsonCompatibility.CreateResolver(),
    };

    public static async Task Run()
    {
        using var fixture = new AutonomousFixture();
        var now = fixture.Time.Now;
        var rules = fixture.Rules with { Version = "adaptive-intraday-v2" };
        fixture.Store.State = fixture.Store.State with { Rules = rules, RulesHash = TradingRuleHash.Compute(rules) };
        var legacy = await fixture.Activate();
        Check(legacy.AiPolicy is null, "Legacy activation must not opt in to AI changes.");
        await fixture.Control.Disable(false, CancellationToken.None);
        var state = await fixture.Control.Activate(
            fixture.Account.AccountKey,
            fixture.Store.State.RulesHash,
            now.AddHours(1),
            "enable-ai-managed-trading",
            CancellationToken.None,
            aiManaged: true
        );
        var authority =
            state.AiPolicy ?? throw new InvalidOperationException("Explicit activation missing AI authority.");
        var mandate = state.Mandate ?? throw new InvalidOperationException("Activation missing mandate.");
        Check(
            TradingAuthority.Valid(state, fixture.Account.AccountKey, now),
            "Explicit AI authorization starts valid."
        );
        var parameters = new TradingPolicyParameters(5, 50, 20, 200, 100, 100, 1.5m, 120, 15, 15, 1m, 1m, .5m, .2m, 1m);
        var candidate = new TradingPolicyCandidate(
            Guid.NewGuid(),
            fixture.Account.AccountKey,
            state.RulesHash,
            now,
            now.AddMinutes(20),
            ["evidence-1"],
            "Reviewed settings with account-scoped evidence.",
            parameters
        );
        var evaluator = new TradingPolicyAcceptedEvaluator(now);
        var store = new FakeTradingStore(state);
        using var gate = new SemaphoreSlim(1, 1);
        TradingPolicyService Service(
            FakeTradingStore target,
            TradingPolicyCandidate value,
            TradingPolicyAcceptedEvaluator evaluation
        ) =>
            new(
                target,
                new TradingPolicyCandidateReader(value),
                evaluation,
                new FakeOrderStore(),
                fixture.Account,
                gate,
                fixture.Time
            );

        await Service(store, candidate, evaluator).Review(CancellationToken.None);
        var applied = store.State;
        Check(
            applied.PolicyChanges.Count == 1 && applied.AiPolicy?.Status == "applied",
            "Reviewed flat-account candidate applies atomically."
        );
        Check(
            applied.Mandate == mandate
                && applied.RulesHash != mandate.RulesHash
                && TradingAuthority.Valid(applied, fixture.Account.AccountKey, now),
            "New effective hash preserves original mandate and explicit lineage."
        );
        Check(
            applied.Rules.Adaptive is { Risk.MaximumExposureFraction: 1m, Risk.MaximumConcentrationFraction: 1m },
            "Capital never exceeds 100%."
        );
        Check(
            applied.Rules.FeeBps == rules.FeeBps
                && applied.Rules.MaxDataAgeSeconds == rules.MaxDataAgeSeconds
                && applied.Rules.Adaptive?.UniverseVersion == rules.Adaptive?.UniverseVersion,
            "Technical candidates cannot change fees, freshness or exchange scope."
        );
        Check(
            !TradingAuthority.Valid(applied with { PolicyChanges = [] }, fixture.Account.AccountKey, now),
            "Removing lineage invalidates changed rules."
        );
        Check(
            !TradingAuthority.Valid(
                applied with
                {
                    AiPolicy = authority with { CapitalCeilingFraction = 1.01m },
                },
                fixture.Account.AccountKey,
                now
            ),
            "Forged ceiling rejected."
        );
        Check(
            !TradingAuthority.Valid(
                applied with
                {
                    Mandate = mandate with { RevokedAt = now },
                },
                fixture.Account.AccountKey,
                now
            ),
            "Revocation invalidates execution and tuning."
        );
        var calls = evaluator.Calls;
        await Service(store, candidate, evaluator).Review(CancellationToken.None);
        Check(
            evaluator.Calls == calls && store.State.PolicyChanges.Count == 1,
            "Persisted admission survives worker restart and prevents repeat evaluation."
        );

        foreach (
            var invalid in new[]
            {
                candidate with
                {
                    AccountKey = "other",
                },
                candidate with
                {
                    BaselineRulesHash = new string('f', 64),
                },
                candidate with
                {
                    ExpiresAt = now,
                },
                candidate with
                {
                    AsOf = now.AddSeconds(1),
                },
                candidate with
                {
                    EvidenceIds = ["duplicate", "duplicate"],
                },
                candidate with
                {
                    Parameters = parameters with { MaximumExposureFraction = 1.01m },
                },
            }
        )
        {
            var rejected = new FakeTradingStore(state);
            await Service(rejected, invalid, new(now)).Review(CancellationToken.None);
            Check(
                rejected.State.RulesHash == state.RulesHash && rejected.State.PolicyChanges.Count == 0,
                "Invalid candidate must not alter execution settings."
            );
        }

        var uncertain = new TradingIntent(
            Guid.NewGuid(),
            "ETH",
            Quantara.Portfolio.Domain.TradeSide.Buy,
            100m,
            100m,
            100m,
            now,
            now.ToUnixTimeSeconds(),
            state.RulesHash,
            "original",
            "unknown"
        );
        foreach (
            var blocked in new[]
            {
                state with
                {
                    Live = state.Live with { Orders = [uncertain] },
                },
                state with
                {
                    Live = state.Live with { Positions = [new TradingHolding("ETH", 1m, 100m, now, 100m)] },
                },
                state with
                {
                    AiPolicy = null,
                },
                state with
                {
                    Mode = TradingMode.Stopped,
                },
                state with
                {
                    Mandate = mandate with { ExpiresAt = now },
                },
            }
        )
        {
            var deferred = new FakeTradingStore(blocked);
            var check = new TradingPolicyAcceptedEvaluator(now);
            await Service(deferred, candidate, check).Review(CancellationToken.None);
            Check(
                check.Calls == 0 && deferred.State.RulesHash == state.RulesHash,
                "Unresolved exposure or absent authority cannot start evaluation."
            );
        }

        foreach (
            var change in new Func<TradingState, TradingState>[]
            {
                current =>
                    current with
                    {
                        Mode = TradingMode.Stopped,
                        Mandate = mandate with { RevokedAt = now },
                        AiPolicy = TradingPolicyAuthority.Revoke(current, now),
                    },
                current =>
                    current with
                    {
                        Autonomy = current.Autonomy is { Capital: { } capital } autonomy
                            ? autonomy with
                            {
                                Capital = capital with { SpendableThb = 50m },
                            }
                            : null,
                    },
            }
        )
        {
            var changed = new FakeTradingStore(state);
            var check = new TradingPolicyAcceptedEvaluator(now)
            {
                BeforeReturn = () =>
                    changed.State = change(changed.State) with { Revision = changed.State.Revision + 1 },
            };
            await Service(changed, candidate, check).Review(CancellationToken.None);
            Check(
                changed.State.RulesHash == state.RulesHash && changed.State.PolicyChanges.Count == 0,
                "Stop or capital changes during evaluation prevent application."
            );
        }

        var lost = new FakeTradingStore(state);
        lost.BeforeSave = next =>
        {
            if (next.PolicyChanges.Count > 0)
            {
                lost.State = lost.State with { Revision = lost.State.Revision + 1 };
            }
        };
        await Service(lost, candidate, new(now)).Review(CancellationToken.None);
        Check(lost.State.PolicyChanges.Count == 0, "CAS loss must never report or apply a policy change.");
        var failed = new FakeTradingStore(state);
        await Service(failed, candidate, new(now) { Accepted = false }).Review(CancellationToken.None);
        Check(
            failed.State.PolicyReview is { Evaluation.Accepted: false } && failed.State.RulesHash == state.RulesHash,
            "Unknown or rejected comparisons retain current settings with evidence."
        );

        var restored =
            JsonSerializer.Deserialize<TradingState>(JsonSerializer.Serialize(applied))
            ?? throw new InvalidOperationException("Roundtrip missing.");
        Check(
            TradingAuthority.Valid(restored, fixture.Account.AccountKey, now),
            "AI authority and lineage survive persisted JSON restart."
        );
        var oldJson = JsonSerializer.Serialize(TradingStateFactory.Create(now), TradingPolicyChecks.legacyOptions);
        Check(
            !oldJson.Contains("AiPolicy", StringComparison.Ordinal)
                && !oldJson.Contains("PolicyChanges", StringComparison.Ordinal),
            "Legacy bytes omit optional AI properties."
        );
        var history = new TradingHistoryStub();
        var historical = new TradingPolicyEvaluator(history, fixture.Markets, fixture.Time);
        var autonomy = state.Autonomy ?? throw new InvalidOperationException("Missing activation capital.");
        var observed = state with
        {
            Autonomy = autonomy with { Markets = [new("ETH", now, "observed", rules.Version)] },
        };
        var incomplete = await historical.Evaluate(observed, parameters.Apply(rules), CancellationToken.None);
        Check(
            !incomplete.Accepted
                && history.Calls == 3
                && incomplete.Baseline.Count == 18
                && incomplete.Candidate.SequenceEqual(incomplete.Baseline)
                && incomplete.RiskScenarios.Count == 2,
            "One fetch per native frame must feed both policies and real risk scenarios; insufficient closed samples must not be promoted."
        );
        fixture.Store.State = applied;
        await fixture.Control.Disable(true, CancellationToken.None);
        Check(
            fixture.Store.State.AiPolicy?.RevokedAt == now
                && !TradingAuthority.Valid(fixture.Store.State, fixture.Account.AccountKey, now),
            "Emergency stop revokes AI and execution together."
        );
        Console.WriteLine(
            "AI policy checks passed: opt-in, atomic apply, original lineage, ceiling, invalid candidates, exposure, stop, changed capital, CAS, restart, rejection and legacy serialization."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
