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
    IReadOnlyOrderStore? legacyOrders = null,
    AutonomousMarketMonitor? autonomous = null,
    AutonomousRiskService? adaptiveRisk = null,
    TradingEventDispatcher? events = null
)
{
    private readonly TradingStateWriter writer = new(store);

    private readonly TradingOrderReconciler reconciler = new(store, exchange, time, adaptiveRisk);

    private readonly TradingLiveExecution liveExecution = new(store, exchange, account, time, adaptiveRisk, events);

    private readonly TradingPaperExecution paperExecution = new(store);

    private readonly TradingSignalProcessor signals = new(store, account, adaptiveRisk);

    public Task Tick(CancellationToken cancellationToken) => this.Run(null, cancellationToken);

    public Task Process(TradingMarketEvent marketEvent, CancellationToken cancellationToken) =>
        this.Run(marketEvent, cancellationToken);

    public Task PrepareReconciliation(CancellationToken cancellationToken) =>
        this.reconciler.Prepare(cancellationToken);

    private async Task Run(TradingMarketEvent? trigger, CancellationToken cancellationToken)
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
                    state = await this.writer.Save(
                        state,
                        TradingAuthority.Fault(state, "Recovered after prolonged trading outage", now),
                        cancellationToken
                    );
                }

                if (state.Mode == TradingMode.AutoLive && !TradingAuthority.Valid(state, account.AccountKey, now))
                {
                    state = await this.writer.Save(
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
                    state = await this.writer.Save(
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

                var entries = trigger is null || trigger.Kind == TradingEventKind.CandleClosed;
                if (trigger is null || trigger.Kind == TradingEventKind.Reconcile)
                {
                    state = await this.reconciler.Reconcile(state, now, trigger is not null, cancellationToken);
                    events?.RecordReconciliation(time.GetUtcNow());
                }

                var adaptive = state.Rules.Adaptive;
                IReadOnlyList<TradingBook>? observed = trigger is null ? null : events?.LatestBooks() ?? [];
                if (
                    trigger is
                    { Kind: TradingEventKind.BookChanged or TradingEventKind.QuoteChanged, Book: { } eventBook }
                )
                {
                    observed = (observed ?? [])
                        .Where(book => book.Symbol != eventBook.Symbol)
                        .Append(eventBook)
                        .ToArray();
                }

                var scan = adaptive is not null
                    ? await (
                        autonomous ?? throw new InvalidOperationException("Autonomous market monitor is unavailable.")
                    ).Read(
                        state,
                        observed,
                        trigger is null
                            || trigger.Kind == TradingEventKind.Research
                            || trigger.Kind == TradingEventKind.CandleClosed && trigger.Sequence > 0,
                        cancellationToken
                    )
                    : null;
                if (scan is not null)
                {
                    var funded = TradingLedgerValuation.ApplyFunding(state, scan.Capital);
                    state = await this.writer.Save(state, funded with { Autonomy = scan.Status }, cancellationToken);
                }

                var books =
                    scan?.Books
                    ?? (trigger is null ? await markets.Read(state.Rules.Symbols, cancellationToken) : observed ?? []);
                now = time.GetUtcNow();
                if (books.Select(book => book.Symbol).Distinct(StringComparer.Ordinal).Count() != books.Count)
                {
                    throw new InvalidDataException("Duplicate market identity.");
                }

                var stale =
                    adaptive is null
                    && state.Rules.Symbols.Any(symbol =>
                        !books.Any(book => book.Symbol == symbol && TradingExecutionRules.Fresh(book, state.Rules, now))
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
                    Paper = TradingLedgerValuation.Mark(state.Paper, books, state.Rules, now),
                    Live =
                        state.Live.FundingThb > 0
                            ? TradingLedgerValuation.Mark(state.Live, books, state.Rules, now)
                            : state.Live,
                };
                var currentBook = state.Mode == TradingMode.AutoLive ? next.Live : next.Paper;
                var dailyLoss = TradingLedgerValuation.DailyResult(
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
                else if (dailyLoss <= -TradingLedgerValuation.LossCap(state))
                {
                    next = TradingAuthority.Fault(next, "Daily loss limit reached", now);
                }
                else
                {
                    next = next with { Fault = null };
                }

                state = await this.writer.Save(state, next, cancellationToken);
                state = await this.paperExecution.Fill(state, books, now, cancellationToken);
                var studyBooks = books
                    .Concat(
                        (scan?.ExitStudyBooks ?? []).Select(history =>
                            books.SingleOrDefault(book => book.Symbol == history.Symbol) is { } quote
                                ? quote with
                                {
                                    Bars = history.Bars,
                                    TimeframeSeconds = history.TimeframeSeconds,
                                }
                                : history
                        )
                    )
                    .ToArray();
                var study = TradingExitStudy.Advance(state, studyBooks, now);
                if (!study.ExitStudies.SequenceEqual(state.ExitStudies))
                {
                    state = await this.writer.Save(state, study, cancellationToken);
                }

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
                        state = await this.writer.Save(state, state with { Shadows = reviewed }, cancellationToken);
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

                var evaluated = await this.signals.Process(state, books, scan, trigger, stale, now, cancellationToken);
                state = evaluated.State;
                var exitEvaluated = evaluated.ExitEvaluated;

                if (
                    trigger is
                    {
                        Kind: TradingEventKind.BookChanged
                            or TradingEventKind.QuoteChanged
                            or TradingEventKind.CandleClosed
                    }
                )
                {
                    events?.RecordDecision(
                        System.Diagnostics.Stopwatch.GetElapsedTime(trigger.GetReceivedTimestamp()),
                        time.GetUtcNow(),
                        exitEvaluated
                    );
                }

                if (live)
                {
                    state = await this.liveExecution.Execute(state, books, now, entries, cancellationToken);
                }
                else
                {
                    state = await this.paperExecution.Fill(state, books, now, cancellationToken);
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
                    await this.writer.Save(
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
                await this.writer.Save(
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
}
