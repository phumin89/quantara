using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingEngine(
    ITradingStore store,
    ITradingMarketReader markets,
    ITradingExchange exchange,
    IAccountContext account,
    SemaphoreSlim gate,
    TimeProvider time,
    IReadOnlyOrderStore? legacyOrders = null,
    AutonomousMarketMonitor? autonomous = null,
    AutonomousRiskService? adaptiveRisk = null
)
{
    public async Task Tick(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.Read(cancellationToken);
            var now = time.GetUtcNow();
            state.Rules.Validate();
            if (state.RulesHash != TradingRuleHash.Compute(state.Rules))
            {
                throw new InvalidDataException("Stored trading rules do not match their version hash.");
            }

            try
            {
                if (state.HeartbeatAt is not null && now - state.HeartbeatAt > TimeSpan.FromMinutes(3))
                {
                    state = await this.Save(
                        state,
                        TradingAuthority.Fault(state, "Recovered after prolonged trading outage", now),
                        cancellationToken
                    );
                }

                if (state.Mode == TradingMode.AutoLive && !TradingAuthority.Valid(state, account.AccountKey, now))
                {
                    state = await this.Save(
                        state,
                        TradingAuthority.Fault(
                            state with
                            {
                                Mode = TradingMode.Stopped,
                            },
                            "Mandate expired or invalid",
                            now
                        ),
                        cancellationToken
                    );
                }

                if (
                    !TradingAuthority.Valid(state, account.AccountKey, now)
                    && state.Live.Orders.Any(order => order.Status == "pending")
                )
                {
                    state = await this.Save(
                        state,
                        state with
                        {
                            Live = state.Live with
                            {
                                Orders = state
                                    .Live.Orders.Select(order =>
                                        order.Status == "pending" ? order with { Status = "cancelled" } : order
                                    )
                                    .ToArray(),
                            },
                        },
                        cancellationToken
                    );
                }

                state = await this.Reconcile(state, now, cancellationToken);
                var adaptive = state.Rules.Adaptive;
                var scan = adaptive is not null
                    ? await (
                        autonomous ?? throw new InvalidOperationException("Autonomous market monitor is unavailable.")
                    ).Read(state, cancellationToken)
                    : null;
                if (scan is not null)
                {
                    var funded = ApplyFunding(state, scan.Capital);
                    state = await this.Save(state, funded with { Autonomy = scan.Status }, cancellationToken);
                }

                var books = scan?.Books ?? await markets.Read(state.Rules.Symbols, cancellationToken);
                now = time.GetUtcNow();
                if (books.Select(book => book.Symbol).Distinct(StringComparer.Ordinal).Count() != books.Count)
                {
                    throw new InvalidDataException("Duplicate market identity.");
                }

                var stale =
                    adaptive is null
                    && state.Rules.Symbols.Any(symbol =>
                        !books.Any(book => book.Symbol == symbol && Fresh(book, state.Rules, now))
                    );
                var uncertain = state.Live.Orders.Any(order =>
                    order.Status is "submitting" or "unknown" || order.CancellationState is "claimed" or "unknown"
                );
                var legacyUncertain =
                    legacyOrders is not null
                    && (await legacyOrders.List(cancellationToken)).Any(order =>
                        order.State is OrderState.Submitting or OrderState.Unknown
                    );
                var next = state with
                {
                    HeartbeatAt = now,
                    Paper = Mark(state.Paper, books, state.Rules, now),
                    Live = state.Live.FundingThb > 0 ? Mark(state.Live, books, state.Rules, now) : state.Live,
                };
                var currentBook = state.Mode == TradingMode.AutoLive ? next.Live : next.Paper;
                var dailyLoss = DailyResult(
                    currentBook,
                    TradingLedgerAccounting.Equity(currentBook, books, now, state.Rules.MaxDataAgeSeconds),
                    now
                );
                if (stale)
                {
                    next = TradingAuthority.Fault(next, "Stale or unavailable market feed", now);
                }
                else if (uncertain || legacyUncertain)
                {
                    next = TradingAuthority.Fault(next, "Uncertain broker order — reconciliation required", now);
                }
                else if (dailyLoss <= -LossCap(state))
                {
                    next = TradingAuthority.Fault(next, "Daily loss limit reached", now);
                }
                else
                {
                    next = next with { Fault = null };
                }

                state = await this.Save(state, next, cancellationToken);
                state = await this.FillPaper(state, books, now, cancellationToken);
                if (adaptive is not null)
                {
                    var reviewed = state
                        .Shadows.Where(sample => sample.CreatedAt >= now.AddDays(-7))
                        .Select(sample =>
                            TradingShadowProtocol.Advance(
                                sample,
                                books.SingleOrDefault(book => book.Symbol == sample.Symbol),
                                now
                            )
                        )
                        .ToArray();
                    if (!reviewed.SequenceEqual(state.Shadows))
                    {
                        state = await this.Save(state, state with { Shadows = reviewed }, cancellationToken);
                    }
                }

                if (state.Mode is not (TradingMode.Paper or TradingMode.AutoLive) || state.EmergencyStopped)
                {
                    return;
                }

                var live = state.Mode == TradingMode.AutoLive;
                if (
                    live
                    && (
                        !TradingAuthority.Valid(state, account.AccountKey, time.GetUtcNow())
                        || uncertain
                        || legacyUncertain
                    )
                )
                {
                    return;
                }

                var ranked = adaptive is null ? state.Rules.Symbols : RankMarkets(state, books, now);
                foreach (var symbol in ranked)
                {
                    var book = books.SingleOrDefault(item => item.Symbol == symbol);
                    var ledger = live ? state.Live : state.Paper;
                    var position = ledger.Positions.SingleOrDefault(item => item.Symbol == symbol && item.Quantity > 0);
                    if (book is null || !Fresh(book, state.Rules, now, position is null))
                    {
                        continue;
                    }

                    if (ledger.Orders.Any(order => order.Symbol == symbol && TradingLedgerAccounting.Active(order)))
                    {
                        continue;
                    }

                    var decision = Evaluate(state.Rules, book, position, now);
                    if (
                        adaptive is not null
                        && decision.Action == TradingAction.Buy
                        && decision.StopPrice is not null
                        && scan is not null
                        && scan.EntrySymbols.Contains(symbol, StringComparer.Ordinal)
                    )
                    {
                        var sampleKey =
                            state.RulesHash
                            + ":"
                            + symbol
                            + ":"
                            + decision.Candle.ToString(CultureInfo.InvariantCulture)
                            + ":"
                            + decision.StrategyVersion;
                        if (
                            !state.Shadows.Any(sample => sample.Key == sampleKey)
                            && state.Shadows.Count(sample =>
                                sample.State is TradingShadowState.Waiting or TradingShadowState.Open
                            ) < 8
                        )
                        {
                            state = await this.Save(
                                state,
                                state with
                                {
                                    Shadows = state
                                        .Shadows.Append(
                                            TradingShadowProtocol.Capture(sampleKey, state.Rules, book, decision, now)
                                        )
                                        .ToArray(),
                                },
                                cancellationToken
                            );
                        }
                    }

                    if (decision.Action == TradingAction.Sell)
                    {
                        decision = decision with { Candle = now.ToUnixTimeSeconds() / 300 * 300 - 300 };
                    }

                    if (decision.Action == TradingAction.Buy)
                    {
                        if (scan is not null && !scan.EntrySymbols.Contains(symbol, StringComparer.Ordinal))
                        {
                            decision = decision with
                            {
                                Action = TradingAction.Wait,
                                Reason = "Owned market monitored for exits only; outside the entry universe.",
                            };
                        }

                        var equity = TradingLedgerAccounting.Equity(ledger, books, now, state.Rules.MaxDataAgeSeconds);
                        var loss = DailyResult(ledger, equity, now);
                        var slots = ledger
                            .Positions.Where(item => item.Quantity > 0)
                            .Select(item => item.Symbol)
                            .Concat(ledger.Orders.Where(TradingLedgerAccounting.Active).Select(item => item.Symbol))
                            .Distinct(StringComparer.Ordinal)
                            .Count();
                        var previous = ledger
                            .Orders.Where(order => order.Symbol == symbol)
                            .OrderBy(order => order.CreatedAt)
                            .LastOrDefault();
                        if (loss is null || loss <= -LossCap(state))
                        {
                            decision = decision with
                            {
                                Action = TradingAction.Wait,
                                Reason = "Daily loss or unknown valuation",
                            };
                        }
                        else if (
                            stale
                            || slots >= state.Rules.MaxPositions
                            || adaptive is null
                                && ledger.Positions.Sum(item => item.CostThb)
                                    + TradingLedgerAccounting.ReservedCash(ledger)
                                    + state.Rules.PerOrderThb
                                    > state.Rules.AllocatedThb
                            || adaptive is null
                                && ledger.CashThb - TradingLedgerAccounting.ReservedCash(ledger)
                                    < state.Rules.PerOrderThb
                            || previous is not null
                                && now - (previous.LastInspectedAt ?? previous.CreatedAt)
                                    < TimeSpan.FromMinutes(state.Rules.CooldownMinutes)
                        )
                        {
                            decision = decision with
                            {
                                Action = TradingAction.Wait,
                                Reason = "Capacity, available cash, cooldown or feed guard",
                            };
                        }
                    }

                    if (state.Autonomy is { } status)
                    {
                        state = await this.Save(
                            state,
                            state with
                            {
                                Autonomy = status with
                                {
                                    Markets = status
                                        .Markets.Where(item => item.Symbol != symbol)
                                        .Append(
                                            new TradingScanObservation(
                                                symbol,
                                                now,
                                                decision.Reason,
                                                decision.StrategyVersion,
                                                AdaptiveTradingStrategy.OpportunityScore(state.Rules, book, decision)
                                            )
                                        )
                                        .ToArray(),
                                },
                            },
                            cancellationToken
                        );
                    }

                    var key = Key(state, symbol, decision);
                    if (state.Decisions.Any(item => item.Key == key))
                    {
                        continue;
                    }

                    var record = new TradingDecisionRecord(
                        key,
                        symbol,
                        decision.Candle,
                        decision.Action,
                        decision.Reason,
                        now,
                        state.Mode
                    );
                    next = state with { Decisions = state.Decisions.Append(record).ToArray() };
                    if (decision.Action != TradingAction.Wait)
                    {
                        var side = decision.Action == TradingAction.Buy ? TradeSide.Buy : TradeSide.Sell;
                        var limit = Floor(
                            (
                                side == TradeSide.Buy && decision.EntryLimit is { } restingLimit ? restingLimit
                                : side == TradeSide.Buy ? book.Ask * (1 + state.Rules.SlippageBps / 10000m)
                                : book.Bid * (1 - state.Rules.SlippageBps / 10000m)
                            ),
                            book.PriceStep
                        );
                        var intentId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
                        RiskSizingSnapshot? sizing = null;
                        if (adaptive is not null && side == TradeSide.Buy)
                        {
                            sizing = await (
                                adaptiveRisk
                                ?? throw new InvalidOperationException("Autonomous risk service is unavailable.")
                            ).Size(
                                intentId,
                                state.Rules,
                                book,
                                limit,
                                new RiskSignal(
                                    decision.StopPrice,
                                    decision.StopCandle,
                                    DateTimeOffset.FromUnixTimeSeconds(decision.Candle + 300),
                                    decision.StrategyVersion ?? decision.Reason
                                ),
                                decision.EntryLimit is not null,
                                cancellationToken,
                                live
                                    ? null
                                    : PaperCapitalSnapshot.Create(
                                        account.AccountKey,
                                        state.Paper,
                                        books,
                                        state.Rules,
                                        now
                                    )
                            );
                            if (!sizing.Decision.Accepted)
                            {
                                next = next with
                                {
                                    Autonomy = next.Autonomy is { } rejectedStatus
                                        ? rejectedStatus with
                                        {
                                            Markets = rejectedStatus
                                                .Markets.Select(item =>
                                                    item.Symbol == symbol
                                                        ? item with
                                                        {
                                                            Reason = sizing.Decision.Explanation,
                                                        }
                                                        : item
                                                )
                                                .ToArray(),
                                        }
                                        : null,
                                    Decisions = state
                                        .Decisions.Append(
                                            record with
                                            {
                                                Action = TradingAction.Wait,
                                                Reason = sizing.Decision.Explanation,
                                            }
                                        )
                                        .ToArray(),
                                };
                                state = await this.Save(state, next, cancellationToken);
                                continue;
                            }
                        }

                        var amount =
                            side == TradeSide.Buy
                                ? Floor(sizing?.Decision.Amount ?? state.Rules.PerOrderThb, Scale(book.QuoteScale))
                                : Floor(
                                    Math.Min(
                                        (
                                            position
                                            ?? throw new InvalidDataException("Exit requires an owned position.")
                                        ).Quantity,
                                        adaptive is not null ? position.Quantity : state.Rules.PerOrderThb / limit
                                    ),
                                    Scale(book.BaseScale)
                                );
                        var market = new OrderMarket(
                            book.Bid,
                            book.MinimumNotional,
                            book.PriceStep,
                            book.PriceScale,
                            side == TradeSide.Buy ? book.QuoteScale : book.BaseScale,
                            side == TradeSide.Sell && adaptive is not null
                                ? book.SellEnabled ?? book.Enabled
                                : book.Enabled
                        );
                        if (
                            side == TradeSide.Sell
                            && adaptive is null
                            && position is not null
                            && position.Quantity * limit > state.Rules.PerOrderThb
                            && (position.Quantity - amount) * limit < book.MinimumNotional
                        )
                        {
                            amount = Floor(position.Quantity - book.MinimumNotional / limit, Scale(book.BaseScale));
                        }

                        var intent = new TradingIntent(
                            intentId,
                            symbol,
                            side,
                            amount,
                            limit,
                            amount,
                            now,
                            decision.Candle,
                            state.RulesHash,
                            decision.Reason,
                            "pending",
                            Sizing: sizing
                        );
                        try
                        {
                            OrderRules.Validate(Proposal(intent), market);
                            var updated = ledger with { Orders = ledger.Orders.Append(intent).ToArray() };
                            next = live ? next with { Live = updated } : next with { Paper = updated };
                        }
                        catch (OrderValidationException)
                        {
                            next = next with
                            {
                                Decisions = state
                                    .Decisions.Append(
                                        record with
                                        {
                                            Action = TradingAction.Wait,
                                            Reason = "Market minimum or precision guard",
                                        }
                                    )
                                    .ToArray(),
                            };
                        }
                    }

                    state = await this.Save(state, next, cancellationToken);
                }

                if (live)
                {
                    state = await this.Execute(state, books, now, cancellationToken);
                }
                else
                {
                    state = await this.FillPaper(state, books, now, cancellationToken);
                }

                if (state.Autonomy is { } finalStatus)
                {
                    var ledger = live ? state.Live : state.Paper;
                    var active = ledger.Orders.Count(TradingLedgerAccounting.Active);
                    var activity =
                        state.Fault
                        ?? finalStatus.Capital?.Blocker
                        ?? (
                            active > 0 ? $"Monitoring {active} open order(s); fills are confirmed with the broker."
                            : ledger.Positions.Any(position => position.Quantity > 0)
                                ? "Monitoring positions and evaluating exit conditions."
                            : finalStatus.UniverseAt is null ? finalStatus.Activity
                            : "Scanning markets; no entry has passed signal, cost and capital checks yet."
                        );
                    await this.Save(
                        state,
                        state with
                        {
                            Autonomy = finalStatus with
                            {
                                Activity = activity,
                                ObservedAt = time.GetUtcNow(),
                                NextCheckAt = time.GetUtcNow().AddSeconds(10),
                                Capital = live
                                    ? finalStatus.Capital
                                    : PaperCapitalSnapshot.Create(
                                        account.AccountKey,
                                        state.Paper,
                                        books,
                                        state.Rules,
                                        now
                                    ),
                            },
                        },
                        cancellationToken
                    );
                }
            }
            catch (Exception error)
                when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Re-read after any failed persistence: a claim may already be durable.
                var latest = await store.Read(cancellationToken);
                await this.Save(
                    latest,
                    TradingAuthority.Fault(
                        latest with
                        {
                            HeartbeatAt = now,
                        },
                        "Trading dependency or reconciliation failure — new entries paused",
                        now
                    ),
                    cancellationToken
                );
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static string[] RankMarkets(TradingState state, IReadOnlyList<TradingBook> books, DateTimeOffset now)
    {
        var ledger = state.Mode == TradingMode.AutoLive ? state.Live : state.Paper;
        return books
            .Select(book =>
            {
                var holding = ledger.Positions.SingleOrDefault(position =>
                    position.Symbol == book.Symbol && position.Quantity > 0
                );
                var decision = Evaluate(state.Rules, book, holding, now);
                return (
                    book.Symbol,
                    Exit: decision.Action == TradingAction.Sell,
                    Score: AdaptiveTradingStrategy.OpportunityScore(state.Rules, book, decision)
                );
            })
            .OrderByDescending(item => item.Exit)
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .Select(item => item.Symbol)
            .ToArray();
    }

    private async Task<TradingState> Reconcile(
        TradingState state,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var original in state
                .Live.Orders.Where(order => TradingLedgerAccounting.Active(order) && order.Status != "pending")
                .ToArray()
        )
        {
            if (original.LastInspectedAt is not null && now - original.LastInspectedAt < TimeSpan.FromSeconds(30))
            {
                continue;
            }

            var observation = await exchange.Inspect(original, cancellationToken);
            var inspectedAt = time.GetUtcNow();
            if (observation.Status is not ("unknown" or "submitted" or "partial" or "filled" or "cancelled"))
            {
                throw new InvalidDataException("Invalid reconciliation state.");
            }

            if (
                observation.Status != "unknown"
                && (
                    observation.BrokerOrderId is null
                    || observation.Remaining is null
                    || observation.Remaining < 0
                    || observation.Remaining > original.Amount
                )
            )
            {
                throw new InvalidDataException("Incomplete broker order reconciliation.");
            }

            var ledger = state.Live;
            foreach (
                var fill in observation
                    .Fills.OrderBy(fill => fill.OccurredAt)
                    .ThenBy(fill => fill.BrokerFillId, StringComparer.Ordinal)
            )
            {
                if (
                    fill.IntentId != original.Id
                    || fill.Symbol != original.Symbol
                    || fill.Side != original.Side
                    || fill.OccurredAt < original.CreatedAt.AddMinutes(-1)
                    || fill.OccurredAt > inspectedAt.AddSeconds(5)
                    || fill.Side == TradeSide.Buy && fill.Price > original.LimitPrice
                    || fill.Side == TradeSide.Sell && fill.Price < original.LimitPrice
                )
                {
                    throw new InvalidDataException("Fill identity, time or limit mismatch.");
                }

                ledger = TradingLedgerAccounting.Apply(ledger, fill);
                if (fill.Side == TradeSide.Buy && original.Sizing is { } sizing)
                {
                    ledger = ledger with
                    {
                        Positions = ledger
                            .Positions.Select(position =>
                                position.Symbol == original.Symbol
                                    ? position with
                                    {
                                        StopPrice = sizing.Signal.StopPrice,
                                        TargetPrice =
                                            original.LimitPrice
                                            + decimal.Max(
                                                sizing.Market.Atr * 2,
                                                original.LimitPrice * state.Rules.TakeProfitBps / 10000m
                                            ),
                                        TrailingDistance = decimal.Max(
                                            sizing.Market.Atr,
                                            original.LimitPrice * state.Rules.TrailingStopBps / 10000m
                                        ),
                                    }
                                    : position
                            )
                            .ToArray(),
                    };
                }
            }

            var fills = ledger.Fills.Where(fill => fill.IntentId == original.Id).ToArray();
            var used =
                original.Side == TradeSide.Buy
                    ? fills.Sum(fill => fill.GrossThb + fill.FeeThb)
                    : fills.Sum(fill => fill.Quantity);
            if (
                used > original.Amount + (original.Side == TradeSide.Buy ? 0.00000001m : 0.000000000000000001m)
                || observation.Status == "filled" && (fills.Length == 0 || observation.Remaining != 0)
            )
            {
                throw new InvalidDataException("Fill totals do not reconcile to submitted amount.");
            }

            var intent = original with
            {
                Status = observation.Status,
                BrokerOrderId = observation.BrokerOrderId ?? original.BrokerOrderId,
                Remaining = observation.Remaining ?? original.Remaining,
                LastInspectedAt = inspectedAt,
            };
            if (!TradingLedgerAccounting.Active(intent))
            {
                intent = intent with { CancellationState = intent.CancellationState is null ? null : "confirmed" };
            }

            var accountChanged = ledger.Fills.Count != state.Live.Fills.Count || intent.Status != original.Status;
            var notices = ledger
                .Fills.Where(fill => !state.Live.Fills.Any(previous => previous.BrokerFillId == fill.BrokerFillId))
                .Select(fill => TradingFillNotice.Create(fill, inspectedAt))
                .Where(notice => !state.Alerts.Any(alert => alert.Key == notice.Key));
            state = await this.Save(
                state,
                state with
                {
                    Live = Replace(ledger, intent),
                    Alerts = state.Alerts.Concat(notices).ToArray(),
                },
                cancellationToken
            );
            if (accountChanged)
            {
                adaptiveRisk?.Invalidate();
            }
        }

        return state;
    }

    private async Task<TradingState> Execute(
        TradingState state,
        IReadOnlyList<TradingBook> books,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        foreach (var original in state.Live.Orders.Where(TradingLedgerAccounting.Active).ToArray())
        {
            if (!TradingAuthority.Valid(state, account.AccountKey, time.GetUtcNow()))
            {
                return state;
            }

            var book = books.SingleOrDefault(item => item.Symbol == original.Symbol);
            if (
                original.Status == "pending"
                && time.GetUtcNow() - original.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes)
            )
            {
                state = await this.Save(
                    state,
                    state with
                    {
                        Live = Replace(state.Live, original with { Status = "cancelled" }),
                    },
                    cancellationToken
                );
                continue;
            }

            if (original.Status != "pending")
            {
                var holding = state.Live.Positions.SingleOrDefault(position =>
                    position.Symbol == original.Symbol && position.Quantity > 0
                );
                var exitNeeded =
                    original.Side == TradeSide.Buy
                    && book is not null
                    && (
                        (holding is not null && Evaluate(state.Rules, book, holding, now).Action == TradingAction.Sell)
                        || original.Sizing?.Signal.StopPrice is { } stop && book.Bid <= stop
                    );
                if (
                    original.Status is "submitted" or "partial"
                    && original.CancellationState is null
                    && (
                        exitNeeded || now - original.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes)
                    )
                )
                {
                    using var prepared = await exchange.PrepareCancellation(original, cancellationToken);
                    var claimed = original with { CancellationState = "claimed" };
                    state = await this.Save(
                        state,
                        state with
                        {
                            Live = Replace(state.Live, claimed),
                        },
                        cancellationToken
                    );
                    var cancellationAuthority = await store.Read(cancellationToken);
                    if (
                        !TradingAuthority.Valid(cancellationAuthority, account.AccountKey, time.GetUtcNow())
                        || cancellationAuthority.Mandate?.Id != state.Mandate?.Id
                        || cancellationAuthority.Live.Orders.Single(order => order.Id == original.Id).CancellationState
                            != "claimed"
                    )
                    {
                        return cancellationAuthority;
                    }

                    state = cancellationAuthority;
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                        var result = await prepared.Send(timeout.Token);
                        claimed = claimed with { CancellationState = result.Accepted ? "acknowledged" : "unknown" };
                    }
                    catch (Exception)
                    {
                        claimed = claimed with { CancellationState = "unknown" };
                    }

                    state = await this.Save(
                        state,
                        state with
                        {
                            Live = Replace(state.Live, claimed),
                        },
                        CancellationToken.None
                    );
                }

                continue;
            }

            if (
                book is null
                || !Fresh(book, state.Rules, now, original.Side != TradeSide.Sell)
                || original.RulesHash != state.RulesHash
                || state.Live.Orders.Any(order =>
                    order.Status is "submitting" or "unknown" || order.CancellationState is "claimed" or "unknown"
                )
            )
            {
                return state;
            }

            var market = await exchange.Market(original.Symbol, original.Side, cancellationToken);
            OrderRules.Validate(Proposal(original), market);
            var balances = await exchange.Balances(cancellationToken);
            if (!Fresh(book, state.Rules, time.GetUtcNow(), original.Side != TradeSide.Sell))
            {
                return state;
            }

            if (
                original.Side == TradeSide.Buy
                && DailyResult(
                    state.Live,
                    TradingLedgerAccounting.Equity(state.Live, books, time.GetUtcNow(), state.Rules.MaxDataAgeSeconds),
                    time.GetUtcNow()
                )
                    is null
            )
            {
                return state;
            }

            if (
                original.Side == TradeSide.Buy
                && DailyResult(
                    state.Live,
                    TradingLedgerAccounting.Equity(state.Live, books, time.GetUtcNow(), state.Rules.MaxDataAgeSeconds),
                    time.GetUtcNow()
                ) <= -LossCap(state)
            )
            {
                state = await this.Save(
                    state,
                    state with
                    {
                        Live = Replace(state.Live, original with { Status = "cancelled" }),
                    },
                    cancellationToken
                );
                continue;
            }

            var currency = original.Side == TradeSide.Buy ? "THB" : original.Symbol;
            if (original.Side == TradeSide.Buy && state.Rules.Adaptive is not null)
            {
                var retained = original.Sizing;
                var current =
                    retained is not null
                    && retained.PolicyHash == state.RulesHash
                    && retained.AccountKey == account.AccountKey
                        ? await (
                            adaptiveRisk
                            ?? throw new InvalidOperationException("Autonomous risk service is unavailable.")
                        ).Size(
                            original.Id,
                            state.Rules,
                            book,
                            original.LimitPrice,
                            retained.Signal,
                            retained.Market.Resting,
                            cancellationToken
                        )
                        : null;
                if (current is null || !current.Decision.Accepted || current.Decision.Amount < original.Amount)
                {
                    state = await this.Save(
                        state,
                        state with
                        {
                            Live = Replace(state.Live, original with { Status = "cancelled" }),
                        },
                        cancellationToken
                    );
                    continue;
                }
            }

            var available = balances.SingleOrDefault(balance => balance.Symbol == currency)?.Available;
            var reserved = state
                .Live.Orders.Where(order =>
                    order.Id != original.Id && TradingLedgerAccounting.Active(order) && order.Side == TradeSide.Buy
                )
                .Sum(order => order.Remaining);
            if (
                available is null
                || available < original.Amount
                || original.Side == TradeSide.Buy && state.Live.CashThb - reserved < original.Amount
                || original.Side == TradeSide.Buy
                    && state.Rules.Adaptive is null
                    && state.Live.Positions.Sum(position => position.CostThb) + reserved + original.Amount
                        > state.Rules.AllocatedThb
                || state.Live.Positions.Any(position =>
                    position.Quantity > 0
                    && (balances.SingleOrDefault(balance => balance.Symbol == position.Symbol)?.Quantity ?? -1)
                        < position.Quantity
                )
            )
            {
                throw new InvalidDataException("Available funds or strategy position reconciliation mismatch.");
            }

            using var submission = await exchange.Prepare(original, cancellationToken);
            var intent = original with { Status = "submitting" };
            if (!TradingAuthority.Valid(state, account.AccountKey, time.GetUtcNow()))
            {
                return state;
            }

            // This durable CAS is the only entry into the autonomous broker POST path.
            state = await this.Save(state, state with { Live = Replace(state.Live, intent) }, cancellationToken);
            var submissionAuthority = await store.Read(cancellationToken);
            if (
                !TradingAuthority.Valid(submissionAuthority, account.AccountKey, time.GetUtcNow())
                || submissionAuthority.Mandate?.Id != state.Mandate?.Id
                || submissionAuthority.Live.Orders.Single(order => order.Id == intent.Id).Status != "submitting"
            )
            {
                return submissionAuthority;
            }

            state = submissionAuthority;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var result = await submission.Send(timeout.Token);
                intent = intent with
                {
                    Status = result.Accepted ? "submitted" : "rejected",
                    BrokerOrderId = result.OrderId,
                    LastInspectedAt = now,
                };
            }
            catch (Exception)
            {
                intent = intent with { Status = "unknown", LastInspectedAt = now };
            }

            adaptiveRisk?.Invalidate();
            var next = state with { Live = Replace(state.Live, intent) };
            if (intent.Status == "unknown")
            {
                next = TradingAuthority.Fault(next, "Uncertain broker order — reconciliation required", now);
            }

            state = await this.Save(state, next, CancellationToken.None);
        }

        return state;
    }

    private async Task<TradingState> FillPaper(
        TradingState state,
        IReadOnlyList<TradingBook> books,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var ledger = state.Paper;
        foreach (var order in ledger.Orders.Where(order => order.Status is "pending" or "partial").ToArray())
        {
            if (now - order.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes))
            {
                ledger = Replace(ledger, order with { Status = "cancelled", LastInspectedAt = now });
                continue;
            }

            var book = books.SingleOrDefault(book => book.Symbol == order.Symbol);
            if (
                book is null
                || !Fresh(book, state.Rules, now, order.Side != TradeSide.Sell)
                || book.ObservedAt <= order.LastFillObservation
            )
            {
                continue;
            }

            var holding = ledger.Positions.SingleOrDefault(position =>
                position.Symbol == order.Symbol && position.Quantity > 0
            );
            if (
                order.Side == TradeSide.Buy
                && holding is not null
                && Evaluate(state.Rules, book, holding, now).Action == TradingAction.Sell
            )
            {
                ledger = Replace(ledger, order with { Status = "cancelled", LastInspectedAt = now });
                continue;
            }

            var execution = PaperFillCalculator.Fill(
                order.Side,
                order.Remaining,
                order.LimitPrice,
                book,
                state.Rules,
                book.BaseScale
            );
            if (execution is null)
            {
                continue;
            }

            var fill = new TradingFill(
                Guid.NewGuid(),
                order.Id,
                "paper-"
                    + order.Id.ToString("N")
                    + "-"
                    + book.ObservedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                order.Symbol,
                order.Side,
                execution.BaseQuantity,
                order.Side == TradeSide.Buy ? book.Ask : book.Bid,
                order.Side == TradeSide.Buy ? execution.GrossThb - execution.FeeThb : execution.GrossThb,
                execution.FeeThb,
                now
            );
            ledger = TradingLedgerAccounting.Apply(ledger, fill);
            if (order.Side == TradeSide.Buy && order.Sizing is { } sizing)
            {
                ledger = ledger with
                {
                    Positions = ledger
                        .Positions.Select(position =>
                            position.Symbol == order.Symbol
                                ? position with
                                {
                                    StopPrice = sizing.Signal.StopPrice,
                                    TargetPrice =
                                        order.LimitPrice
                                        + decimal.Max(
                                            sizing.Market.Atr * 2,
                                            order.LimitPrice * state.Rules.TakeProfitBps / 10000m
                                        ),
                                    TrailingDistance = decimal.Max(
                                        sizing.Market.Atr,
                                        order.LimitPrice * state.Rules.TrailingStopBps / 10000m
                                    ),
                                }
                                : position
                        )
                        .ToArray(),
                };
            }

            var remaining =
                order.Remaining - (order.Side == TradeSide.Buy ? execution.GrossThb : execution.BaseQuantity);
            var dust = order.Side == TradeSide.Buy ? 0.01m : 0m;
            ledger = Replace(
                ledger,
                order with
                {
                    Remaining = remaining,
                    Status = remaining <= dust ? "filled" : "partial",
                    LastFillObservation = book.ObservedAt,
                    LastInspectedAt = now,
                }
            );
        }

        ledger = ledger == state.Paper ? ledger : Mark(ledger, books, state.Rules, now, true);
        return ledger == state.Paper ? state : await this.Save(state, state with { Paper = ledger }, cancellationToken);
    }

    private static TradingLedger Mark(
        TradingLedger ledger,
        IReadOnlyList<TradingBook> books,
        TradingRuleSet rules,
        DateTimeOffset now,
        bool force = false
    )
    {
        var positions = ledger
            .Positions.Select(position =>
            {
                var book = books.SingleOrDefault(book => book.Symbol == position.Symbol);
                return book is not null && Fresh(book, rules, now, false)
                    ? position with
                    {
                        PeakBid = Math.Max(position.PeakBid, book.Bid),
                    }
                    : position;
            })
            .ToArray();
        var marks = ledger.Equity;
        if (force || marks.Count == 0 || now - marks[^1].At >= TimeSpan.FromMinutes(1))
        {
            if (marks.Count > 0 && marks[^1].At == now)
            {
                marks = marks.Take(marks.Count - 1).ToArray();
            }

            marks = marks
                .Append(
                    new TradingEquityMark(
                        now,
                        TradingLedgerAccounting.Equity(ledger, books, now, rules.MaxDataAgeSeconds),
                        ledger.FundingThb
                    )
                )
                .ToArray();
        }

        return ledger with
        {
            Positions = positions,
            Equity = marks,
        };
    }

    private static bool Fresh(TradingBook book, TradingRuleSet rules, DateTimeOffset now, bool entry = true) =>
        (rules.Adaptive is not null && !entry ? book.SellEnabled ?? book.Enabled : book.Enabled)
        && book.Bid > 0
        && book.Ask >= book.Bid
        && book.PriceStep > 0
        && book.BaseScale is >= 0 and <= 18
        && book.QuoteScale is >= 0 and <= 18
        && book.PriceScale is >= 0 and <= 18
        && book.ObservedAt <= now
        && now - book.ObservedAt <= TimeSpan.FromSeconds(rules.MaxDataAgeSeconds)
        && (rules.Adaptive is not null && !entry || (book.Ask - book.Bid) / book.Bid * 10000m <= rules.MaxSpreadBps);

    private static decimal? DailyResult(TradingLedger ledger, decimal? equity, DateTimeOffset now)
    {
        if (equity is null)
        {
            return null;
        }

        var today = now.ToOffset(TimeSpan.FromHours(7)).Date;
        var sells = ledger
            .Fills.Where(fill =>
                fill.Side == TradeSide.Sell && fill.OccurredAt.ToOffset(TimeSpan.FromHours(7)).Date == today
            )
            .ToArray();
        return sells.Any(fill => fill.RealizedNetThb is null)
            ? null
            : sells.Sum(fill => fill.RealizedNetThb ?? 0)
                + equity
                - ledger.CashThb
                - ledger.Positions.Sum(position => position.CostThb);
    }

    private static decimal LossCap(TradingState state) =>
        state.Rules.Adaptive is { } policy
            ? (
                state.Mode == TradingMode.Paper
                    ? (state.Paper.Equity.Count > 0 ? state.Paper.Equity[^1].EquityThb : null) ?? state.Paper.FundingThb
                    : state.Autonomy?.Capital?.EquityThb ?? 0
            ) * policy.DailyLossFraction
            : state.Rules.DailyLossThb;

    private static TradingDecision Evaluate(
        TradingRuleSet rules,
        TradingBook book,
        TradingHolding? holding,
        DateTimeOffset now
    ) =>
        rules.Adaptive is null
            ? TradingStrategy.Evaluate(rules, book, holding, now)
            : AdaptiveTradingStrategy.Evaluate(rules, book, holding, now);

    private static TradingState ApplyFunding(TradingState state, CapitalReadResult capital)
    {
        if (state.Mode != TradingMode.AutoLive)
        {
            return state;
        }

        var unseen = capital
            .Evidence.CashFlows.Where(flow => !state.ObservedFundingFlows.Contains(flow.Id, StringComparer.Ordinal))
            .ToArray();
        var adjustment = unseen
            .Where(flow => flow.Symbol == "THB")
            .Sum(flow => flow.Kind == CashFlowKind.Deposit ? flow.Quantity : -flow.Quantity);
        return state with
        {
            Live = state.Live with
            {
                CashThb = state.Live.CashThb + adjustment,
                FundingThb = state.Live.FundingThb + adjustment,
            },
            ObservedFundingFlows = state
                .ObservedFundingFlows.Concat(unseen.Select(flow => flow.Id))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static TradingLedger Replace(TradingLedger ledger, TradingIntent intent) =>
        ledger with
        {
            Orders = ledger.Orders.Select(order => order.Id == intent.Id ? intent : order).ToArray(),
        };

    private static string Key(TradingState state, string symbol, TradingDecision decision) =>
        state.Mode
        + ":"
        + state.RulesHash
        + ":"
        + symbol
        + ":"
        + decision.Candle.ToString(CultureInfo.InvariantCulture)
        + ":"
        + decision.Action;

    private static decimal Floor(decimal value, decimal step) =>
        step > 0 ? Math.Floor(value / step) * step : throw new InvalidDataException("Invalid market precision.");

    private static decimal Scale(int scale)
    {
        if (scale is < 0 or > 18)
        {
            throw new InvalidDataException("Invalid market scale.");
        }

        var step = 1m;
        for (var i = 0; i < scale; i++)
        {
            step /= 10;
        }

        return step;
    }

    private static OrderProposal Proposal(TradingIntent intent) =>
        new(
            intent.Id,
            intent.Symbol,
            intent.Side,
            intent.Amount,
            intent.LimitPrice,
            intent.LimitPrice,
            intent.Reason,
            intent.CreatedAt,
            DateTimeOffset.MaxValue,
            OrderState.Submitted,
            intent.BrokerOrderId,
            null
        );

    private async Task<TradingState> Save(TradingState before, TradingState next, CancellationToken cancellationToken)
    {
        next = next with { Revision = before.Revision + 1 };
        if (!await store.Save(next, before.Revision, cancellationToken))
        {
            throw new InvalidOperationException("Trading state changed concurrently; no new side effect authorized.");
        }

        return next;
    }
}
