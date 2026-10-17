using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class AutonomousMarketMonitor(ITradingMarketReader markets, AutonomousRiskService risk, TimeProvider time)
{
    private TradingUniverse? universe;

    private Task<AutonomousTradingBatch>? scanning;

    private string? scanningHash;

    private AutonomousTradingBatch? latest;

    private string? latestHash;

    private long version;

    public long Version => Volatile.Read(ref this.version);

    public long? LatestClosedCandle =>
        Volatile
            .Read(ref this.latest)
            ?.Books.Where(book => book.Bars.Count > 0)
            .Select(book => (long?)book.Bars[^1].Timestamp)
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

        if (this.scanning is null && startScan)
        {
            this.scanningHash = hash;
            this.scanning = this.ReadCore(state, cancellationToken);
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
        var eligible =
            current is not null
            && current.Status.UniverseAt is { } universeAt
            && universeAt <= now
            && now - universeAt <= TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
                ? current.EntrySymbols
                : [];
        return new(
            status,
            books
                .Select(book =>
                    book with
                    {
                        Bars = current?.Books.SingleOrDefault(item => item.Symbol == book.Symbol)?.Bars ?? book.Bars,
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
                                }
                                : book
                        )
                )
                .ToArray(),
            eligible,
            current?.Capital ?? emptyCapital
        );
    }

    private async Task<AutonomousTradingBatch> ReadCore(TradingState state, CancellationToken cancellationToken)
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
                    .Shadows.Where(sample => sample.State is TradingShadowState.Waiting or TradingShadowState.Open)
                    .Select(sample => sample.Symbol)
            )
            .Concat(next)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var books = new List<TradingBook>();
        foreach (var batch in requested.Chunk(16))
        {
            books.AddRange(await markets.Read(batch, cancellationToken));
        }

        if (
            books.Any(book => !requested.Contains(book.Symbol, StringComparer.Ordinal))
            || books.Select(book => book.Symbol).Distinct(StringComparer.Ordinal).Count() != books.Count
        )
        {
            throw new InvalidDataException("Unexpected or duplicate execution market identity.");
        }

        var capital = await risk.Read(state.Rules, false, cancellationToken);
        now = time.GetUtcNow();
        var missing = next.Where(symbol => !books.Any(book => book.Symbol == symbol))
            .Select(symbol => new TradingScanObservation(
                symbol,
                now,
                "Preparing verified candles or order book; other markets continue.",
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
        return new(
            new(
                now,
                valid ? universe.ObservedAt : null,
                universe.TotalMarkets,
                eligible.Length,
                combined,
                capital.Snapshot,
                activity,
                now.AddMinutes(1)
            ),
            books,
            eligible,
            capital
        );
    }
}
