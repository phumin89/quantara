using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingControl(
    ITradingStore store,
    ITradingExchange exchange,
    ITradingMarketReader markets,
    IAccountContext account,
    TradingControlOptions options,
    SemaphoreSlim gate,
    TimeProvider time,
    IReadOnlyOrderStore? reviewedOrders = null,
    AutonomousRiskService? adaptiveRisk = null
) : ITradingControl
{
    public async Task<TradingState> Configure(TradingRuleSet rules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.Read(cancellationToken);
            return await this.Configure(state, rules, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TradingState> PrepareAdaptive(
        string accountKey,
        string expectedRulesHash,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(accountKey)
            || accountKey.Length > 128
            || string.IsNullOrWhiteSpace(expectedRulesHash)
            || expectedRulesHash.Length != 64
            || expectedRulesHash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
        )
        {
            throw new ArgumentException("Review the current account and rules before preparing adaptive trading.");
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.Read(cancellationToken);
            if (
                accountKey != account.AccountKey
                || expectedRulesHash != state.RulesHash
                || expectedRulesHash != TradingRuleHash.Compute(state.Rules)
            )
            {
                throw new ArgumentException(
                    "Review the current account and unchanged rules before preparing adaptive trading."
                );
            }

            var rules = AdaptiveTradingSetup.Prepare(state.Rules);
            rules.Validate();
            return await this.Configure(state, rules, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TradingState> SetMode(TradingMode mode, CancellationToken cancellationToken)
    {
        if (mode is not (TradingMode.Stopped or TradingMode.Paper or TradingMode.ManualReview))
        {
            throw new ArgumentException("Enable AutoLive with a reviewed mandate.");
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.Read(cancellationToken);
            if (mode != TradingMode.Stopped && state.Live.Orders.Any(TradingLedgerAccounting.Active))
            {
                throw new ArgumentException("Reconcile outstanding live orders before changing mode.");
            }

            return await this.Save(
                state,
                state with
                {
                    Mode = mode,
                    EmergencyStopped = false,
                    Mandate = state.Mandate is null ? null : state.Mandate with { RevokedAt = time.GetUtcNow() },
                    AiPolicy = TradingPolicyAuthority.Revoke(state, time.GetUtcNow()),
                },
                cancellationToken
            );
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TradingState> Activate(
        string accountKey,
        string rulesHash,
        DateTimeOffset expiresAt,
        string acknowledgement,
        CancellationToken cancellationToken,
        bool aiManaged = false
    )
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.Read(cancellationToken);
            var now = time.GetUtcNow();
            if (
                string.IsNullOrWhiteSpace(options.OwnerToken)
                || acknowledgement != (aiManaged ? "enable-ai-managed-trading" : "enable-auto-trading")
                || accountKey != account.AccountKey
                || rulesHash != state.RulesHash
                || rulesHash != TradingRuleHash.Compute(state.Rules)
            )
            {
                throw new ArgumentException("Review the current account and unchanged rules before enabling.");
            }

            if (expiresAt <= now || expiresAt > now.AddDays(30))
            {
                throw new ArgumentException("Choose an expiry within the next 30 days.");
            }

            if (aiManaged && state.Rules.Version != "adaptive-intraday-v2")
            {
                throw new ArgumentException("AI-managed strategy requires adaptive-intraday-v2.");
            }

            if (TradingAuthority.Valid(state, accountKey, now))
            {
                if (state.Mandate?.ExpiresAt == expiresAt && aiManaged == (state.AiPolicy is not null))
                {
                    return state;
                }

                throw new ArgumentException("Disable the current mandate before reviewing a different expiry.");
            }

            state.Rules.Validate();
            if (TradingCashBaselineAccounting.CashUnknown(state.Live))
            {
                throw new ArgumentException(
                    "Review current broker cash or verified sale settlement after retiring external inventory. Capital was not reset."
                );
            }

            if (state.Rules.ExitPolicy is not null && !options.AdaptiveExitLiveAvailable)
            {
                throw new ArgumentException(
                    "Adaptive exits are experimental. Complete the shadow review before live activation."
                );
            }

            if (state.Live.Orders.Any(TradingLedgerAccounting.Active))
            {
                throw new ArgumentException("Reconcile all outstanding strategy orders before activation.");
            }

            if (
                reviewedOrders is not null
                && (await reviewedOrders.Exposure(now, cancellationToken)).Any(order =>
                    order.Sizing is not null || order.State is OrderState.Submitting or OrderState.Unknown
                )
            )
            {
                throw new ArgumentException(
                    "Reconcile uncertain manual submissions and complete adaptive manual orders before enabling Auto Trading."
                );
            }

            if (state.Rules.Adaptive is not null)
            {
                return await this.ActivateAdaptive(state, expiresAt, aiManaged, cancellationToken);
            }

            var books = await markets.Read(state.Rules.Symbols, cancellationToken);
            now = time.GetUtcNow();
            if (expiresAt <= now)
            {
                throw new ArgumentException("The mandate expired while verifying markets.");
            }

            if (
                state.Rules.Symbols.Any(symbol =>
                    !books.Any(book =>
                        book.Symbol == symbol
                        && book.Enabled
                        && book.Bid > 0
                        && book.Ask >= book.Bid
                        && book.ObservedAt <= now
                        && now - book.ObservedAt <= TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
                    )
                )
            )
            {
                throw new ArgumentException("Fresh enabled markets are required for every allowed symbol.");
            }

            var balances = await exchange.Balances(cancellationToken);
            now = time.GetUtcNow();
            if (
                expiresAt <= now
                || books.Any(book =>
                    book.ObservedAt > now || now - book.ObservedAt > TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
                )
            )
            {
                throw new ArgumentException(
                    "Fresh market evidence and an unexpired mandate are required after balance verification."
                );
            }

            var live = state.Live;
            var requiredCash = live.FundingThb > 0 ? live.CashThb : state.Rules.AllocatedThb;
            if (
                balances.SingleOrDefault(balance => balance.Symbol == "THB")?.Available is not decimal cash
                || cash < requiredCash
                || !await this.PositionsCovered(live, balances, cancellationToken)
            )
            {
                throw new ArgumentException(
                    "Verified available balances do not cover the strategy allocation and positions."
                );
            }

            now = time.GetUtcNow();
            if (
                expiresAt <= now
                || books.Any(book =>
                    book.ObservedAt > now || now - book.ObservedAt > TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
                )
            )
            {
                throw new ArgumentException(
                    "Fresh market evidence and an unexpired mandate are required after position verification."
                );
            }

            if (live.FundingThb == 0)
            {
                live = new TradingLedger(
                    state.Rules.AllocatedThb,
                    state.Rules.AllocatedThb,
                    now,
                    [],
                    [],
                    [],
                    [new TradingEquityMark(now, state.Rules.AllocatedThb, state.Rules.AllocatedThb)]
                );
            }

            return await this.Save(
                state,
                state with
                {
                    Mode = TradingMode.AutoLive,
                    EmergencyStopped = false,
                    Fault = null,
                    Live = live,
                    Mandate = new TradingMandate(Guid.NewGuid(), accountKey, rulesHash, now, expiresAt, null),
                    AiPolicy = null,
                },
                cancellationToken
            );
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TradingState> ActivateAdaptive(
        TradingState state,
        DateTimeOffset expiresAt,
        bool aiManaged,
        CancellationToken cancellationToken
    )
    {
        var service = adaptiveRisk ?? throw new InvalidOperationException("Autonomous risk service is unavailable.");
        var policy = state.Rules.Adaptive ?? throw new InvalidOperationException("An adaptive policy is required.");
        var universe = await markets.ReadUniverse(cancellationToken);
        var capital = await service.Read(state.Rules, true, cancellationToken);
        var now = time.GetUtcNow();
        if (
            expiresAt <= now
            || universe.Error is not null
            || universe.ObservedAt > now
            || now - universe.ObservedAt > TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
            || capital.Snapshot.Blocker is not null
            || capital.Snapshot.HasUncertainOrders
            || capital.Snapshot.EquityThb is null or <= 0
            || capital.Snapshot.SpendableThb is null
            || capital.Snapshot.AccountKey != account.AccountKey
        )
        {
            throw new ArgumentException(
                capital.Snapshot.Blocker
                    ?? "Fresh verified account capital and market universe are required before enabling."
            );
        }

        if (!await this.PositionsCovered(state.Live, capital.Evidence.Balances, cancellationToken))
        {
            throw new ArgumentException("Reconcile existing owned positions before enabling the new policy.");
        }

        now = time.GetUtcNow();
        if (
            expiresAt <= now
            || universe.ObservedAt > now
            || now - universe.ObservedAt > TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
            || capital.Evidence.ObservedAt > now
            || now - capital.Evidence.ObservedAt > TimeSpan.FromSeconds(policy.Risk.MaximumDataAgeSeconds)
        )
        {
            throw new ArgumentException(
                "Fresh account evidence and an unexpired mandate are required after position verification."
            );
        }

        var cash = capital.Evidence.Balances.Single(balance => balance.Symbol == "THB").Quantity;
        var retained = state.Live.FundingThb != 0m || state.Live.CashBaselines.Count != 0;
        if (retained && !TradingCashBalance.Matches(state.Live, cash))
        {
            throw new ArgumentException(
                "Current broker cash differs from the retained ledger. Review cash reconciliation before activation; funding was not reset."
            );
        }

        var difference = cash - state.Live.CashThb;
        var live = retained
            ? state.Live
            : state.Live with
            {
                CashThb = cash,
                FundingThb = state.Live.FundingThb + difference,
                OpenedAt = state.Live.FundingThb == 0 ? now : state.Live.OpenedAt,
            };
        var eligible = universe.Symbols.Except(policy.ExcludedSymbols, StringComparer.Ordinal).Count();
        var mandate = new TradingMandate(Guid.NewGuid(), account.AccountKey, state.RulesHash, now, expiresAt, null);
        return await this.Save(
            state,
            state with
            {
                Mode = TradingMode.AutoLive,
                EmergencyStopped = false,
                Fault = null,
                Live = live,
                Mandate = mandate,
                AiPolicy = aiManaged
                    ? new TradingPolicyAuthorization(
                        mandate.Id,
                        account.AccountKey,
                        state.RulesHash,
                        state.RulesHash,
                        now,
                        expiresAt
                    )
                    : null,
                ObservedFundingFlows =
                    state.Live.CashBaselines.Count == 0
                        ? capital.Evidence.CashFlows.Select(flow => flow.Id).ToArray()
                        : state.ObservedFundingFlows,
                Autonomy = new(
                    now,
                    universe.ObservedAt,
                    universe.TotalMarkets,
                    eligible,
                    [],
                    capital.Snapshot,
                    "Monitoring markets under the reviewed adaptive policy.",
                    now.AddSeconds(10)
                ),
            },
            cancellationToken
        );
    }

    public async Task<TradingState> Disable(bool emergency, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.Read(cancellationToken);
            var now = time.GetUtcNow();
            var next = state with
            {
                Mode = TradingMode.Stopped,
                EmergencyStopped = emergency,
                Mandate = state.Mandate is null ? null : state.Mandate with { RevokedAt = now },
                AiPolicy = TradingPolicyAuthority.Revoke(state, now),
            };
            if (emergency)
            {
                next = TradingAuthority.Fault(
                    next,
                    "Emergency stop — open broker orders remain subject to reconciliation",
                    now
                );
            }

            return await this.Save(state, next, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TradingState> Configure(
        TradingState state,
        TradingRuleSet rules,
        CancellationToken cancellationToken
    )
    {
        if (TradingRuleHash.Compute(rules) == state.RulesHash)
        {
            return state;
        }

        if (
            (state.Rules.ExitPolicy is not null || rules.ExitPolicy is not null)
            && state.Paper.Positions.Concat(state.Live.Positions).Any(position => position.Quantity > 0)
        )
        {
            throw new ArgumentException("Keep the original exit policy until all owned positions are closed.");
        }

        if (
            state.Paper.Orders.Any(TradingLedgerAccounting.Active)
            || state.Live.Orders.Any(TradingLedgerAccounting.Active)
        )
        {
            throw new ArgumentException("Stop and reconcile outstanding orders before changing rules.");
        }

        if (
            state
                .Paper.Positions.Concat(state.Live.Positions)
                .Any(position =>
                    rules.Adaptive is null
                    && position.Quantity > 0
                    && !rules.Symbols.Contains(position.Symbol, StringComparer.Ordinal)
                )
        )
        {
            throw new ArgumentException("Keep every owned position in the allowed symbols until it is closed.");
        }

        if (
            state.Paper.Positions.Count(position => position.Quantity > 0) > rules.MaxPositions
            || state.Live.Positions.Count(position => position.Quantity > 0) > rules.MaxPositions
        )
        {
            throw new ArgumentException("The position limit cannot be below current exposure.");
        }

        var now = time.GetUtcNow();
        return await this.Save(
            state,
            state with
            {
                Rules = rules,
                RulesHash = TradingRuleHash.Compute(rules),
                Mode = state.Mode == TradingMode.AutoLive ? TradingMode.Stopped : state.Mode,
                Mandate = state.Mandate is null ? null : state.Mandate with { RevokedAt = now },
                Paper = rules.Adaptive is null ? Resize(state.Paper, rules.AllocatedThb, now) : state.Paper,
                Live =
                    rules.Adaptive is null && state.Live.FundingThb > 0
                        ? Resize(state.Live, rules.AllocatedThb, now)
                        : state.Live,
                Autonomy = null,
                AiPolicy = TradingPolicyAuthority.Revoke(state, now),
            },
            cancellationToken
        );
    }

    private async Task<bool> PositionsCovered(
        TradingLedger ledger,
        IReadOnlyList<Balance> balances,
        CancellationToken cancellationToken
    )
    {
        foreach (var position in ledger.Positions.Where(position => position.Quantity > 0))
        {
            if (
                !await TradingPositionBalance.Covers(
                    balances.SingleOrDefault(balance => balance.Symbol == position.Symbol)?.Available,
                    position,
                    exchange,
                    cancellationToken
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static TradingLedger Resize(TradingLedger ledger, decimal allocation, DateTimeOffset now)
    {
        var delta = allocation - ledger.FundingThb;
        if (ledger.CashThb + delta < 0 || allocation < ledger.Positions.Sum(position => position.CostThb))
        {
            throw new ArgumentException("Allocation cannot be below committed position cost.");
        }

        return ledger with
        {
            CashThb = ledger.CashThb + delta,
            FundingThb = allocation,
            Equity = ledger.Equity.Append(new TradingEquityMark(now, null, allocation)).ToArray(),
        };
    }

    private async Task<TradingState> Save(TradingState before, TradingState after, CancellationToken cancellationToken)
    {
        var next = after with { Revision = before.Revision + 1 };
        if (!await store.Save(next, before.Revision, cancellationToken))
        {
            throw new InvalidOperationException("Trading state changed; reload before applying controls.");
        }

        return next;
    }
}
