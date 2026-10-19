using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class AutonomousMarketMonitor(
    ITradingMarketReader markets,
    AutonomousRiskService risk,
    TimeProvider time,
    IPortfolioGoalStore? goals = null,
    IAccountContext? account = null,
    ITradingHistoryReader? exitHistory = null
)
{
    private TradingUniverse? universe;

    private Task<AutonomousTradingBatch>? scanning;

    private string? scanningHash;

    private AutonomousTradingBatch? latest;

    private string? latestHash;

    private Task<CapitalReadResult>? readingCapital;

    private string? readingCapitalHash;

    private CapitalReadResult? latestCapital;

    private string? latestCapitalHash;

    private DateTimeOffset nextCapitalAt;

    private long version;

    public long Version => Volatile.Read(ref this.version);

    public long? LatestClosedCandle =>
        Volatile
            .Read(ref this.latest)
            ?.Books.Where(book => book.Bars.Count > 0)
            .Select(book => (long?)(book.Bars[^1].Timestamp + book.TimeframeSeconds - 300))
            .Max();

    public Task<AutonomousTradingBatch> Read(TradingState state, CancellationToken cancellationToken) =>
        this.Read(state, null, true, cancellationToken);

    public async Task<AutonomousTradingBatch> Read(
        TradingState state,
        IReadOnlyList<TradingBook>? observed,
        bool startScan,
        CancellationToken cancellationToken
    )
    {
        var hash = state.RulesHash;
        await this.RefreshCapital(state, cancellationToken);
        if (this.scanning is { IsCompleted: true } completed)
        {
            try
            {
                var result = await completed;
                if (this.scanningHash == hash)
                {
                    this.latest = result;
                    this.latestHash = hash;
                    Interlocked.Increment(ref this.version);
                }
            }
            catch (Exception error)
                when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                this.latest = null;
                this.latestHash = null;
            }

            this.scanning = null;
        }

        if (
            this.scanning is null
            && startScan
            && (this.readingCapitalHash == hash && this.readingCapital is not null || this.latestCapitalHash == hash)
        )
        {
            this.scanningHash = hash;
            this.scanning = this.ReadCore(
                state,
                this.readingCapital
                    ?? Task.FromResult(
                        this.latestCapital
                            ?? throw new InvalidOperationException("Capital observation has not started.")
                    ),
                cancellationToken
            );
            if (this.scanning.IsCompletedSuccessfully)
            {
                this.latest = await this.scanning;
                this.latestHash = hash;
                Interlocked.Increment(ref this.version);
                this.scanning = null;
            }
        }

        var current = this.latestHash == hash ? this.latest : null;
        var owned = state
            .Live.Positions.Concat(state.Paper.Positions)
            .Where(position => position.Quantity > 0)
            .Select(position => position.Symbol)
            .Concat(
                state
                    .Live.Orders.Concat(state.Paper.Orders)
                    .Where(TradingLedgerAccounting.Active)
                    .Select(order => order.Symbol)
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var books = new List<TradingBook>();
        foreach (var batch in observed is null ? owned.Chunk(16) : [])
        {
            books.AddRange(await markets.ReadValuations(batch, cancellationToken));
        }

        if (observed is not null)
        {
            books.AddRange(observed.Where(book => owned.Contains(book.Symbol, StringComparer.Ordinal)));
        }

        var now = time.GetUtcNow();
        var emptyCapital = new CapitalReadResult(
            new("", now, null, null, null, null, [], false, "Collecting verified account capital."),
            new(now, [], [], [], now),
            []
        );
        var status =
            current?.Status
            ?? new AutonomousTradingStatus(
                now,
                null,
                0,
                0,
                state.Autonomy?.Markets ?? [],
                state.Autonomy?.Capital,
                "Preparing market and account evidence; existing positions remain monitored.",
                now.AddMinutes(1)
            );
        var capital = this.latestCapitalHash == hash ? this.latestCapital : null;
        status = status with { Capital = capital?.Snapshot ?? status.Capital };
        if (current is not null && state.Autonomy is { } previous)
        {
            status = status with
            {
                Markets = status
                    .Markets.Concat(
                        previous.Markets.Where(item =>
                            current.EntrySymbols.Contains(item.Symbol, StringComparer.Ordinal)
                        )
                    )
                    .GroupBy(item => item.Symbol, StringComparer.Ordinal)
                    .Select(group => group.OrderByDescending(item => item.At).First())
                    .ToArray(),
            };
        }

        var eligible =
            current is not null
            && current.Status.UniverseAt is { } universeAt
            && universeAt <= now
            && now - universeAt <= TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
                ? current.EntrySymbols
                : [];
        return new AutonomousTradingBatch(
            status,
            books
                .Select(book =>
                    book with
                    {
                        Bars = current?.Books.SingleOrDefault(item => item.Symbol == book.Symbol)?.Bars ?? book.Bars,
                        TimeframeSeconds =
                            current?.Books.SingleOrDefault(item => item.Symbol == book.Symbol)?.TimeframeSeconds
                            ?? book.TimeframeSeconds,
                    }
                )
                .Concat(
                    (current?.Books ?? [])
                        .Where(book => !owned.Contains(book.Symbol, StringComparer.Ordinal))
                        .Select(book =>
                            observed?.SingleOrDefault(item => item.Symbol == book.Symbol) is { } quote
                                ? quote with
                                {
                                    Bars = book.Bars,
                                    TimeframeSeconds = book.TimeframeSeconds,
                                }
                                : book
                        )
                )
                .ToArray(),
            eligible,
            capital ?? emptyCapital
        )
        {
            ExitStudyBooks = current?.ExitStudyBooks ?? [],
        };
    }

    private async Task RefreshCapital(TradingState state, CancellationToken cancellationToken)
    {
        if (this.readingCapital is { IsCompleted: true } completed)
        {
            try
            {
                var result = await completed;
                if (this.readingCapitalHash == state.RulesHash)
                {
                    this.latestCapital = result;
                    this.latestCapitalHash = state.RulesHash;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Retain its original timestamp; unavailable refreshes never make old capital fresh.
            }

            this.readingCapital = null;
        }

        var now = time.GetUtcNow();
        if (this.readingCapital is null && (this.readingCapitalHash != state.RulesHash || now >= this.nextCapitalAt))
        {
            this.readingCapitalHash = state.RulesHash;
            this.nextCapitalAt = now.AddSeconds(Math.Min(30, state.Rules.MaxDataAgeSeconds / 2));
            this.readingCapital = this.ReadCapital(state.Rules, cancellationToken);
            if (this.readingCapital.IsCompletedSuccessfully)
            {
                this.latestCapital = await this.readingCapital;
                this.latestCapitalHash = state.RulesHash;
                this.readingCapital = null;
            }
        }
    }

    private async Task<CapitalReadResult> ReadCapital(TradingRuleSet rules, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var capital = await risk.Read(rules, false, timeout.Token);
        var now = time.GetUtcNow();
        if (
            goals is not null
            && account is not null
            && now < Quantara.Portfolio.Domain.PortfolioObjective.DeadlineExclusive
        )
        {
            try
            {
                await goals.Save(PortfolioGoalAccounting.Capture(account.AccountKey, capital, now), timeout.Token);
            }
            catch (System.Data.Common.DbException)
            {
                // A failed objective checkpoint stays Unknown and does not interrupt market protection.
            }
        }

        return capital;
    }

    private async Task<AutonomousTradingBatch> ReadCore(
        TradingState state,
        Task<CapitalReadResult> capitalRead,
        CancellationToken cancellationToken
    )
    {
        var now = time.GetUtcNow();
        if (
            this.universe is null
            || this.universe.ObservedAt > now
            || now - this.universe.ObservedAt >= TimeSpan.FromMinutes(1)
        )
        {
            this.universe = await markets.ReadUniverse(cancellationToken);
        }

        now = time.GetUtcNow();
        var universe = this.universe;
        var policy = state.Rules.Adaptive ?? throw new InvalidOperationException("An adaptive policy is required.");
        var valid =
            universe.Error is null
            && universe.ObservedAt <= now
            && now - universe.ObservedAt <= TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds);
        var eligible = valid ? universe.Symbols.Except(policy.ExcludedSymbols, StringComparer.Ordinal).ToArray() : [];
        var retained = state.Autonomy?.Markets ?? [];
        var observations = (
            valid ? retained.Where(item => eligible.Contains(item.Symbol, StringComparer.Ordinal)) : retained
        ).ToArray();
        var owned = state
            .Live.Positions.Concat(state.Paper.Positions)
            .Where(position => position.Quantity > 0)
            .Select(position => position.Symbol)
            .Concat(
                state
                    .Live.Orders.Concat(state.Paper.Orders)
                    .Where(TradingLedgerAccounting.Active)
                    .Select(order => order.Symbol)
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var next = eligible
            .OrderBy(symbol =>
                observations.FirstOrDefault(item => item.Symbol == symbol)?.At ?? DateTimeOffset.MinValue
            )
            .Take(16)
            .ToArray();
        var requested = owned
            .Concat(
                state
                    .ExitStudies.Where(sample => sample.Status is "active" or "triggered")
                    .Select(sample => sample.Symbol)
                    .Distinct(StringComparer.Ordinal)
                    .Take(16)
            )
            .Concat(
                state
                    .Shadows.Where(sample => sample.State is TradingShadowState.Waiting or TradingShadowState.Open)
                    .Select(sample => sample.Symbol)
            )
            .Concat(next)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var books = new List<TradingBook>();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var history = new List<TradingHistoryEvidence>();
        foreach (var batch in requested.Chunk(16))
        {
            var evidence = await markets.ReadEvidence(
                batch,
                cancellationToken,
                state.Rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3"
            );
            books.AddRange(evidence.Books);
            history.AddRange(evidence.History ?? []);
            foreach (var error in evidence.Errors)
            {
                errors.Add(error.Key, error.Value);
            }
        }

        if (
            books.Any(book => !requested.Contains(book.Symbol, StringComparer.Ordinal))
            || books.Select(book => book.Symbol).Distinct(StringComparer.Ordinal).Count() != books.Count
        )
        {
            throw new InvalidDataException("Unexpected or duplicate execution market identity.");
        }

        if (state.Rules.ExitPolicy is not null && exitHistory is not null)
        {
            var activeLedger = state.Mode == TradingMode.Paper ? state.Paper : state.Live;
            foreach (
                var position in activeLedger.Positions.Where(position =>
                    position.Quantity > 0 && position.ExitPlan is not null
                )
            )
            {
                var selected = books.FindIndex(book => book.Symbol == position.Symbol);
                var plan = position.ExitPlan;
                if (selected < 0 || plan is null || books[selected].TimeframeSeconds == plan.TimeframeSeconds)
                {
                    continue;
                }

                IReadOnlyList<TradingBar> bars;
                try
                {
                    var end = DateTimeOffset.FromUnixTimeSeconds(
                        time.GetUtcNow().ToUnixTimeSeconds() / plan.TimeframeSeconds * plan.TimeframeSeconds
                    );
                    bars = await exitHistory.Read(
                        position.Symbol,
                        plan.TimeframeSeconds,
                        end.AddSeconds(-51 * plan.TimeframeSeconds),
                        end,
                        cancellationToken
                    );
                }
                catch (Exception error)
                    when (error is HttpRequestException or InvalidDataException or System.Text.Json.JsonException
                        || error is OperationCanceledException && !cancellationToken.IsCancellationRequested
                    )
                {
                    bars = [];
                    errors[position.Symbol] =
                        "Pinned exit timeframe history unavailable; original protection remains active.";
                }

                books[selected] = books[selected] with { Bars = bars, TimeframeSeconds = plan.TimeframeSeconds };
            }
        }

        var studyBooks = await this.ReadStudyBooks(state, books, cancellationToken);
        var capital = await capitalRead;
        now = time.GetUtcNow();

        var missing = next.Where(symbol => !books.Any(book => book.Symbol == symbol))
            .Select(symbol => new TradingScanObservation(
                symbol,
                now,
                errors.GetValueOrDefault(symbol, "Preparing verified candles or order book; other markets continue."),
                null
            ));
        var combined = observations
            .Where(item => !next.Contains(item.Symbol, StringComparer.Ordinal))
            .Concat(missing)
            .Concat(
                next.Where(symbol => books.Any(book => book.Symbol == symbol))
                    .Select(symbol => new TradingScanObservation(symbol, now, "Evaluating completed candles.", null))
            )
            .ToArray();
        var activity = !valid
            ? universe.Error ?? "Refreshing market universe."
            : capital.Snapshot.Blocker
                ?? (
                    eligible.Length == 0
                        ? "No eligible THB spot markets. Rechecking market availability."
                        : "Scanning eligible THB spot markets."
                );
        return new AutonomousTradingBatch(
            new(
                now,
                valid ? universe.ObservedAt : null,
                universe.TotalMarkets,
                eligible.Length,
                combined,
                capital.Snapshot,
                activity,
                now.AddMinutes(1)
            )
            {
                History = state.Rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3"
                    ? (state.Autonomy?.History ?? [])
                        .Where(item =>
                            eligible.Contains(item.Symbol, StringComparer.Ordinal)
                            && !requested.Contains(item.Symbol, StringComparer.Ordinal)
                        )
                        .Concat(history)
                        .ToArray()
                    : null,
            },
            books,
            eligible,
            capital
        )
        {
            ExitStudyBooks = studyBooks,
        };
    }

    private async Task<IReadOnlyList<TradingBook>> ReadStudyBooks(
        TradingState state,
        List<TradingBook> books,
        CancellationToken cancellationToken
    )
    {
        if (exitHistory is null)
        {
            return [];
        }

        var result = new List<TradingBook>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        foreach (
            var sample in state
                .ExitStudies.Where(sample => sample.Status is "active" or "triggered" && sample.ExitPlan is not null)
                .DistinctBy(sample => (sample.Symbol, sample.ExitPlan?.TimeframeSeconds))
                .Take(16)
        )
        {
            var book = books.SingleOrDefault(book => book.Symbol == sample.Symbol);
            var plan = sample.ExitPlan;
            if (book is null || plan is null || book.TimeframeSeconds == plan.TimeframeSeconds)
            {
                continue;
            }

            try
            {
                var end = DateTimeOffset.FromUnixTimeSeconds(
                    time.GetUtcNow().ToUnixTimeSeconds() / plan.TimeframeSeconds * plan.TimeframeSeconds
                );
                var bars = await exitHistory.Read(
                    sample.Symbol,
                    plan.TimeframeSeconds,
                    end.AddSeconds(-51 * plan.TimeframeSeconds),
                    end,
                    budget.Token
                );
                result.Add(book with { Bars = bars, TimeframeSeconds = plan.TimeframeSeconds });
            }
            catch (Exception error)
                when (error is HttpRequestException or InvalidDataException or System.Text.Json.JsonException
                    || error is OperationCanceledException && !cancellationToken.IsCancellationRequested
                )
            {
                // Read-only study outages do not change the execution books or their authority.
                if (budget.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        return result;
    }
}
