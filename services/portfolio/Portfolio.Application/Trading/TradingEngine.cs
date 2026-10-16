using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingEngine(
    ITradingStore store,
    ITradingMarketReader markets,
    ITradingExchange exchange,
    IAccountContext account,
    SemaphoreSlim gate,
    TimeProvider time,
    IReadOnlyOrderStore? legacyOrders = null
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
                var books = await markets.Read(state.Rules.Symbols, cancellationToken);
                now = time.GetUtcNow();
                if (books.Select(book => book.Symbol).Distinct(StringComparer.Ordinal).Count() != books.Count)
                {
                    throw new InvalidDataException("Duplicate market identity.");
                }

                var stale = state.Rules.Symbols.Any(symbol =>
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
                else if (dailyLoss <= -state.Rules.DailyLossThb)
                {
                    next = TradingAuthority.Fault(next, "Daily loss limit reached", now);
                }
                else
                {
                    next = next with { Fault = null };
                }

                state = await this.Save(state, next, cancellationToken);
                state = await this.FillPaper(state, books, now, cancellationToken);
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

                foreach (var symbol in state.Rules.Symbols)
                {
                    var book = books.SingleOrDefault(item => item.Symbol == symbol);
                    if (book is null || !Fresh(book, state.Rules, now))
                    {
                        continue;
                    }

                    var ledger = live ? state.Live : state.Paper;
                    if (ledger.Orders.Any(order => order.Symbol == symbol && TradingLedgerAccounting.Active(order)))
                    {
                        continue;
                    }

                    var position = ledger.Positions.SingleOrDefault(item => item.Symbol == symbol && item.Quantity > 0);
                    var decision = TradingStrategy.Evaluate(state.Rules, book, position, now);
                    if (decision.Action == TradingAction.Sell)
                    {
                        decision = decision with { Candle = now.ToUnixTimeSeconds() / 300 * 300 - 300 };
                    }

                    if (decision.Action == TradingAction.Buy)
                    {
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
                        if (loss is null || loss <= -state.Rules.DailyLossThb)
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
                            || ledger.Positions.Sum(item => item.CostThb)
                                + TradingLedgerAccounting.ReservedCash(ledger)
                                + state.Rules.PerOrderThb
                                > state.Rules.AllocatedThb
                            || ledger.CashThb - TradingLedgerAccounting.ReservedCash(ledger) < state.Rules.PerOrderThb
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
                                side == TradeSide.Buy
                                    ? book.Ask * (1 + state.Rules.SlippageBps / 10000m)
                                    : book.Bid * (1 - state.Rules.SlippageBps / 10000m)
                            ),
                            book.PriceStep
                        );
                        var amount =
                            side == TradeSide.Buy
                                ? Floor(state.Rules.PerOrderThb, Scale(book.QuoteScale))
                                : Floor(
                                    Math.Min(
                                        (
                                            position
                                            ?? throw new InvalidDataException("Exit requires an owned position.")
                                        ).Quantity,
                                        state.Rules.PerOrderThb / limit
                                    ),
                                    Scale(book.BaseScale)
                                );
                        var market = new OrderMarket(
                            book.Bid,
                            book.MinimumNotional,
                            book.PriceStep,
                            book.PriceScale,
                            side == TradeSide.Buy ? book.QuoteScale : book.BaseScale,
                            book.Enabled
                        );
                        if (
                            side == TradeSide.Sell
                            && position is not null
                            && position.Quantity * limit > state.Rules.PerOrderThb
                            && (position.Quantity - amount) * limit < book.MinimumNotional
                        )
                        {
                            amount = Floor(position.Quantity - book.MinimumNotional / limit, Scale(book.BaseScale));
                        }

                        var intent = new TradingIntent(
                            new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16)),
                            symbol,
                            side,
                            amount,
                            limit,
                            amount,
                            now,
                            decision.Candle,
                            state.RulesHash,
                            decision.Reason,
                            "pending"
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
                    await this.FillPaper(state, books, now, cancellationToken);
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

            state = await this.Save(state, state with { Live = Replace(ledger, intent) }, cancellationToken);
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
            if (original.Status != "pending")
            {
                var holding = state.Live.Positions.SingleOrDefault(position =>
                    position.Symbol == original.Symbol && position.Quantity > 0
                );
                var exitNeeded =
                    original.Side == TradeSide.Buy
                    && book is not null
                    && holding is not null
                    && TradingStrategy.Evaluate(state.Rules, book, holding, now).Action == TradingAction.Sell;
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
                || !Fresh(book, state.Rules, now)
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
            if (!Fresh(book, state.Rules, time.GetUtcNow()))
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
                ) <= -state.Rules.DailyLossThb
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
            if (book is null || !Fresh(book, state.Rules, now) || book.ObservedAt <= order.LastFillObservation)
            {
                continue;
            }

            var holding = ledger.Positions.SingleOrDefault(position =>
                position.Symbol == order.Symbol && position.Quantity > 0
            );
            if (
                order.Side == TradeSide.Buy
                && holding is not null
                && TradingStrategy.Evaluate(state.Rules, book, holding, now).Action == TradingAction.Sell
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
                return book is not null && Fresh(book, rules, now)
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

    private static bool Fresh(TradingBook book, TradingRuleSet rules, DateTimeOffset now) =>
        book.Enabled
        && book.Bid > 0
        && book.Ask >= book.Bid
        && book.PriceStep > 0
        && book.BaseScale is >= 0 and <= 18
        && book.QuoteScale is >= 0 and <= 18
        && book.PriceScale is >= 0 and <= 18
        && book.ObservedAt <= now
        && now - book.ObservedAt <= TimeSpan.FromSeconds(rules.MaxDataAgeSeconds)
        && (book.Ask - book.Bid) / book.Bid * 10000m <= rules.MaxSpreadBps;

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
