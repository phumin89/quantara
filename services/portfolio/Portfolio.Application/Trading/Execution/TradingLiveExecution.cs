using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal sealed class TradingLiveExecution(
    ITradingStore store,
    ITradingExchange exchange,
    IAccountContext account,
    TimeProvider time,
    AutonomousRiskService? adaptiveRisk,
    TradingEventDispatcher? events
)
{
    private readonly TradingStateWriter writer = new(store);

    internal async Task<TradingState> Execute(
        TradingState state,
        IReadOnlyList<TradingBook> books,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var stage = TradingFailureStage.LiveExecution;
        try
        {
            foreach (var tracked in state.Live.Orders.Where(TradingLedgerAccounting.Active).ToArray())
            {
                var original = tracked;
                if (!TradingAuthority.Valid(state, account.AccountKey, time.GetUtcNow()))
                {
                    return state;
                }

                var book = books.SingleOrDefault(item => item.Symbol == original.Symbol);
                if (
                    original.Status == "pending"
                    && (
                        original.BrokerOrderId is not null
                        || original.CancellationState is not null
                        || original.LastFillObservation is not null
                        || original.Remaining != original.Amount
                    )
                )
                {
                    throw new InvalidDataException(
                        "A pending intent with submission or fill evidence requires reconciliation."
                    );
                }

                if (
                    original.Status == "pending"
                    && time.GetUtcNow() - original.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes)
                )
                {
                    state = await this.writer.Save(
                        state,
                        state with
                        {
                            Live = TradingLedgerAccounting.ReplaceOrder(
                                state.Live,
                                original with
                                {
                                    Status = "cancelled",
                                    LastExecutionMessage = "Not submitted: the order expired before execution.",
                                    LastExecutionAt = time.GetUtcNow(),
                                }
                            ),
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
                            (
                                holding is not null
                                && TradingDecisionEvaluation.Evaluate(state.Rules, book, holding, now).Action
                                    == TradingAction.Sell
                            )
                            || original.Sizing?.Signal.StopPrice is { } stop && book.Bid <= stop
                        );
                    if (
                        original.Status is "submitted" or "partial"
                        && original.CancellationState is null
                        && (
                            exitNeeded
                            || now - original.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes)
                        )
                    )
                    {
                        using var prepared = await exchange.PrepareCancellation(original, cancellationToken);
                        var claimed = original with { CancellationState = "claimed" };
                        state = await this.writer.Save(
                            state,
                            state with
                            {
                                Live = TradingLedgerAccounting.ReplaceOrder(state.Live, claimed),
                            },
                            cancellationToken
                        );
                        var cancellationAuthority = await store.Read(cancellationToken);
                        if (
                            !TradingAuthority.Valid(cancellationAuthority, account.AccountKey, time.GetUtcNow())
                            || cancellationAuthority.Mandate?.Id != state.Mandate?.Id
                            || cancellationAuthority
                                .Live.Orders.Single(order => order.Id == original.Id)
                                .CancellationState != "claimed"
                        )
                        {
                            // This call has not entered Send. Never clear a changed or recovered cancellation claim.
                            if (
                                cancellationAuthority.Revision == state.Revision
                                && System.Text.Json.JsonSerializer.Serialize(
                                    cancellationAuthority.Live.Orders.Single(order => order.Id == original.Id)
                                ) == System.Text.Json.JsonSerializer.Serialize(claimed)
                            )
                            {
                                return await this.writer.Save(
                                    cancellationAuthority,
                                    cancellationAuthority with
                                    {
                                        Live = TradingLedgerAccounting.ReplaceOrder(
                                            cancellationAuthority.Live,
                                            original
                                        ),
                                    },
                                    cancellationToken
                                );
                            }

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

                        state = await this.writer.Save(
                            state,
                            state with
                            {
                                Live = TradingLedgerAccounting.ReplaceOrder(state.Live, claimed),
                            },
                            CancellationToken.None
                        );
                    }

                    continue;
                }

                if (
                    book is null
                    || !TradingExecutionRules.Fresh(book, state.Rules, now, original.Side != TradeSide.Sell)
                    || original.RulesHash != state.RulesHash
                    || state.Live.Orders.Any(order =>
                        order.Status is "submitting" or "unknown" || order.CancellationState is "claimed" or "unknown"
                    )
                )
                {
                    return await this.Observe(
                        state,
                        original,
                        "Waiting for a fresh market quote and consistent order state before submission.",
                        cancellationToken
                    );
                }

                stage = TradingFailureStage.BrokerMarket;
                var market = await exchange.Market(original.Symbol, original.Side, cancellationToken);
                stage = TradingFailureStage.ProposalValidation;
                OrderRules.Validate(TradingExecutionRules.Proposal(original), market);
                stage = TradingFailureStage.BrokerBalance;
                var balances = await exchange.Balances(cancellationToken);
                var quote = events?.LatestBook(original.Symbol);
                if (events is not null && quote is null)
                {
                    return await this.Observe(
                        state,
                        original,
                        "Waiting for the live market quote before submission.",
                        cancellationToken
                    );
                }

                book = quote is null ? book : quote with { Bars = book.Bars, TimeframeSeconds = book.TimeframeSeconds };
                if (!TradingExecutionRules.Fresh(book, state.Rules, time.GetUtcNow(), original.Side != TradeSide.Sell))
                {
                    return await this.Observe(
                        state,
                        original,
                        "Waiting for a fresh quote within the saved spread limit.",
                        cancellationToken
                    );
                }

                if (
                    original.Side == TradeSide.Buy
                    && TradingLedgerValuation.DailyResult(
                        state.Live,
                        TradingLedgerAccounting.Equity(
                            state.Live,
                            books,
                            time.GetUtcNow(),
                            state.Rules.MaxDataAgeSeconds
                        ),
                        time.GetUtcNow()
                    )
                        is null
                )
                {
                    return await this.Observe(
                        state,
                        original,
                        "Waiting for verified account valuation before submission.",
                        cancellationToken
                    );
                }

                if (
                    original.Side == TradeSide.Buy
                    && TradingLedgerValuation.DailyResult(
                        state.Live,
                        TradingLedgerAccounting.Equity(
                            state.Live,
                            books,
                            time.GetUtcNow(),
                            state.Rules.MaxDataAgeSeconds
                        ),
                        time.GetUtcNow()
                    ) <= -TradingLedgerValuation.LossCap(state)
                )
                {
                    state = await this.writer.Save(
                        state,
                        state with
                        {
                            Live = TradingLedgerAccounting.ReplaceOrder(
                                state.Live,
                                original with
                                {
                                    Status = "cancelled",
                                    LastExecutionMessage = "Not submitted: the daily loss limit was reached.",
                                    LastExecutionAt = time.GetUtcNow(),
                                }
                            ),
                        },
                        cancellationToken
                    );
                    continue;
                }

                var currency = original.Side == TradeSide.Buy ? "THB" : original.Symbol;
                var executionSizing = original.Sizing;
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
                    if (current?.Decision is { Accepted: false, Code: "liquidity" })
                    {
                        state = await this.Observe(
                            state,
                            original,
                            "Waiting for executable liquidity at the saved limit price; no order has been sent.",
                            cancellationToken
                        );
                        continue;
                    }

                    if (current is null || !current.Decision.Accepted || current.Decision.Amount is not > 0m)
                    {
                        state = await this.Observe(
                            state,
                            original with
                            {
                                Status = "cancelled",
                            },
                            "Not submitted: "
                                + (
                                    current?.Decision.Explanation
                                    ?? "Matching account and policy sizing is unavailable."
                                ),
                            cancellationToken
                        );
                        continue;
                    }

                    var amount = TradingExecutionRules.Floor(
                        Math.Min(original.Amount, current.Decision.Amount.Value),
                        TradingExecutionRules.Scale(market.AmountScale)
                    );
                    if (amount < market.MinimumNotional)
                    {
                        state = await this.Observe(
                            state,
                            original with
                            {
                                Status = "cancelled",
                            },
                            "Not submitted: the permitted amount is below the market minimum.",
                            cancellationToken
                        );
                        continue;
                    }

                    if (amount < original.Amount)
                    {
                        // Keep the immutable original; atomically retire it and persist a smaller unsent intent.
                        var replacementId = Guid.NewGuid();
                        var replacement = original with
                        {
                            Id = replacementId,
                            ReplacesIntentId = original.Id,
                            OriginalAmount = original.OriginalAmount ?? original.Amount,
                            Amount = amount,
                            Remaining = amount,
                            Sizing = current,
                            ExitPlan = original.ExitPlan is { } exitPlan
                                ? exitPlan with
                                {
                                    EntryIntentId = replacementId,
                                }
                                : null,
                            ExitStudyPlan = original.ExitStudyPlan is { } studyPlan
                                ? studyPlan with
                                {
                                    EntryIntentId = replacementId,
                                }
                                : null,
                            LastExecutionMessage =
                                "Amount reduced to the latest permitted size before submission; the limit price is unchanged.",
                            LastExecutionAt = time.GetUtcNow(),
                        };
                        OrderRules.Validate(TradingExecutionRules.Proposal(replacement), market);
                        var ledger = TradingLedgerAccounting.ReplaceOrder(
                            state.Live,
                            original with
                            {
                                Status = "cancelled",
                                LastExecutionMessage =
                                    "Not submitted: replaced by a smaller entry at the same limit price.",
                                LastExecutionAt = time.GetUtcNow(),
                            }
                        );
                        state = await this.writer.Save(
                            state,
                            state with
                            {
                                Live = ledger with { Orders = ledger.Orders.Append(replacement).ToArray() },
                            },
                            cancellationToken
                        );
                        original = replacement;
                    }

                    executionSizing = current;
                }

                stage = TradingFailureStage.PositionOwnership;
                var ownedHolding = state.Live.Positions.SingleOrDefault(position =>
                    position.Symbol == original.Symbol && position.Quantity > 0
                );
                var actualQuantity = balances.SingleOrDefault(balance => balance.Symbol == original.Symbol)?.Quantity;
                if (
                    original.Side == TradeSide.Sell
                    && ownedHolding is not null
                    && actualQuantity is >= 0
                    && !await TradingPositionBalance.Covers(
                        actualQuantity,
                        ownedHolding,
                        exchange,
                        cancellationToken,
                        market.AmountScale
                    )
                )
                {
                    state = await this.writer.Save(
                        state,
                        state with
                        {
                            Live = TradingLedgerAccounting.ReplaceOrder(
                                state.Live,
                                original with
                                {
                                    Status = "cancelled",
                                    LastExecutionMessage =
                                        "Not submitted: the owned position balance requires reconciliation.",
                                    LastExecutionAt = time.GetUtcNow(),
                                }
                            ),
                        },
                        cancellationToken
                    );
                    throw new InvalidDataException(
                        "Owned position balance requires reconciliation; unsubmitted exit cancelled locally."
                    );
                }

                stage = TradingFailureStage.AvailableFunds;
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
                )
                {
                    throw new InvalidDataException("Available funds are unavailable or insufficient.");
                }

                stage = TradingFailureStage.PositionOwnership;
                if (!await this.PositionsReconciled(state.Live, balances, original, market, cancellationToken))
                {
                    throw new InvalidDataException("Strategy position reconciliation mismatch.");
                }

                stage = TradingFailureStage.SubmissionPreparation;
                using var submission = await exchange.Prepare(original, cancellationToken);
                if (time.GetUtcNow() - original.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes))
                {
                    state = await this.writer.Save(
                        state,
                        state with
                        {
                            Live = TradingLedgerAccounting.ReplaceOrder(
                                state.Live,
                                original with
                                {
                                    Status = "cancelled",
                                    LastExecutionMessage = "Not submitted: the order expired during preparation.",
                                    LastExecutionAt = time.GetUtcNow(),
                                }
                            ),
                        },
                        cancellationToken
                    );
                    continue;
                }

                if (!TradingExecutionRules.Fresh(book, state.Rules, time.GetUtcNow(), original.Side != TradeSide.Sell))
                {
                    return await this.Observe(
                        state,
                        original,
                        "Waiting for a fresh quote after order preparation; no order has been sent.",
                        cancellationToken
                    );
                }

                var intent = original with
                {
                    Status = "submitting",
                    LastExecutionMessage = "Submission claimed; awaiting the exchange response.",
                    LastExecutionAt = time.GetUtcNow(),
                };
                if (!TradingAuthority.Valid(state, account.AccountKey, time.GetUtcNow()))
                {
                    return state;
                }

                // This durable CAS is the only entry into the autonomous broker POST path.
                stage = TradingFailureStage.SubmissionClaim;
                state = await this.writer.Save(
                    state,
                    state with
                    {
                        Live = TradingLedgerAccounting.ReplaceOrder(state.Live, intent),
                    },
                    cancellationToken
                );
                events?.RecordSubmission(
                    TimeSpan.FromMilliseconds(Math.Max(0, (time.GetUtcNow() - original.CreatedAt).TotalMilliseconds))
                );
                stage = TradingFailureStage.FinalSubmissionGuard;
                var submissionAuthority = await store.Read(cancellationToken);
                if (
                    !TradingAuthority.Valid(submissionAuthority, account.AccountKey, time.GetUtcNow())
                    || submissionAuthority.Mandate?.Id != state.Mandate?.Id
                    || submissionAuthority.Live.Orders.Single(order => order.Id == intent.Id).Status != "submitting"
                    || time.GetUtcNow() - original.CreatedAt
                        >= TimeSpan.FromMinutes(submissionAuthority.Rules.OrderLifetimeMinutes)
                    || !TradingExecutionRules.Fresh(
                        book,
                        submissionAuthority.Rules,
                        time.GetUtcNow(),
                        original.Side != TradeSide.Sell
                    )
                    || events is not null
                        && (
                            events.LatestBook(original.Symbol) is not { } finalQuote
                            || !TradingExecutionRules.Fresh(
                                finalQuote,
                                state.Rules,
                                time.GetUtcNow(),
                                original.Side != TradeSide.Sell
                            )
                        )
                    || !TradingExecutionRules.SizingCurrent(
                        submissionAuthority,
                        original,
                        executionSizing,
                        account.AccountKey,
                        time.GetUtcNow()
                    )
                )
                {
                    // Only this call knows Send was never entered. A changed or recovered claim stays uncertain.
                    if (
                        submissionAuthority.Revision == state.Revision
                        && submissionAuthority.Live.Orders.Single(order => order.Id == intent.Id)
                            is { Status: "submitting", BrokerOrderId: null, CancellationState: null }
                    )
                    {
                        return await this.writer.Save(
                            submissionAuthority,
                            submissionAuthority with
                            {
                                Live = TradingLedgerAccounting.ReplaceOrder(
                                    submissionAuthority.Live,
                                    intent with
                                    {
                                        Status = "cancelled",
                                        LastExecutionMessage = "Not submitted: final trading checks no longer passed.",
                                        LastExecutionAt = time.GetUtcNow(),
                                    }
                                ),
                            },
                            cancellationToken
                        );
                    }

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
                        LastExecutionMessage = result.Accepted
                            ? "The exchange accepted the order; waiting for fills."
                            : "The exchange rejected the order.",
                        LastExecutionAt = time.GetUtcNow(),
                    };
                }
                catch (Exception)
                {
                    intent = intent with
                    {
                        Status = "unknown",
                        LastInspectedAt = now,
                        LastExecutionMessage =
                            "The exchange response is unknown; reconciliation is required before another submission.",
                        LastExecutionAt = time.GetUtcNow(),
                    };
                }

                adaptiveRisk?.Invalidate();
                var next = state with { Live = TradingLedgerAccounting.ReplaceOrder(state.Live, intent) };
                if (intent.Status == "unknown")
                {
                    next = TradingAuthority.Fault(next, "Uncertain broker order — reconciliation required", now);
                }

                state = await this.writer.Save(state, next, CancellationToken.None);
            }

            return state;
        }
        catch (Exception error)
        {
            error.Data[typeof(TradingFailureStage)] = stage;
            throw;
        }
    }

    private Task<TradingState> Observe(
        TradingState state,
        TradingIntent intent,
        string message,
        CancellationToken cancellationToken
    )
    {
        if (
            state.Live.Orders.Single(order => order.Id == intent.Id) == intent
            && intent.LastExecutionMessage == message
        )
        {
            return Task.FromResult(state);
        }

        return this.writer.Save(
            state,
            state with
            {
                Live = TradingLedgerAccounting.ReplaceOrder(
                    state.Live,
                    intent with
                    {
                        LastExecutionMessage = message,
                        LastExecutionAt = time.GetUtcNow(),
                    }
                ),
            },
            cancellationToken
        );
    }

    private async Task<bool> PositionsReconciled(
        TradingLedger ledger,
        IReadOnlyList<Balance> balances,
        TradingIntent current,
        OrderMarket currentMarket,
        CancellationToken cancellationToken
    )
    {
        foreach (var position in ledger.Positions.Where(position => position.Quantity > 0))
        {
            var quantity = balances.SingleOrDefault(balance => balance.Symbol == position.Symbol)?.Quantity;
            if (
                !await TradingPositionBalance.Covers(
                    quantity,
                    position,
                    exchange,
                    cancellationToken,
                    current.Symbol == position.Symbol && current.Side == TradeSide.Sell
                        ? currentMarket.AmountScale
                        : null
                )
            )
            {
                return false;
            }
        }

        return true;
    }
}
