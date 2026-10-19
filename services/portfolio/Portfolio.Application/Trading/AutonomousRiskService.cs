using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class AutonomousRiskService(
    RiskCapitalReader capital,
    IRiskStore audit,
    IAccountContext account,
    TimeProvider time
)
{
    private RiskAccountEvidence? evidence;

    private string? rulesHash;

    private IReadOnlyList<TradingBook> valuation = [];

    private readonly object cacheGate = new();

    private DateTimeOffset minimumObservedAt;

    public void Invalidate()
    {
        lock (this.cacheGate)
        {
            this.minimumObservedAt = time.GetUtcNow();
        }
    }

    public async Task<CapitalReadResult> Read(
        TradingRuleSet rules,
        bool refresh,
        CancellationToken cancellationToken,
        string? symbol = null,
        Guid? excludeIntent = null
    )
    {
        var policy = rules.Adaptive ?? throw new InvalidOperationException("An adaptive trading policy is required.");
        var now = time.GetUtcNow();
        var hash = TradingRuleHash.Compute(rules);
        RiskAccountEvidence? observed;
        lock (this.cacheGate)
        {
            observed =
                !refresh
                && this.rulesHash == hash
                && this.evidence is { } cached
                && cached.ObservedAt <= now
                && cached.ObservedAt >= this.minimumObservedAt
                && now - cached.ObservedAt < TimeSpan.FromSeconds(Math.Min(45, policy.Risk.MaximumDataAgeSeconds))
                    ? this.evidence
                    : null;
        }

        var result = await capital.Read(
            symbol,
            null,
            policy.Risk,
            cancellationToken,
            observed,
            excludeIntent,
            valuationOnly: true
        );
        if (result.Snapshot.AccountKey != account.AccountKey)
        {
            throw new InvalidDataException("Autonomous capital belongs to another account.");
        }

        lock (this.cacheGate)
        {
            this.evidence = result.Evidence;
            this.rulesHash = hash;
            this.valuation = result.Markets;
        }

        await audit.Observe(
            result.Snapshot,
            result.Evidence.CashFlows,
            result.Evidence.CashFlowCoverageFrom,
            cancellationToken
        );
        return result;
    }

    public async Task<RiskSizingSnapshot> Size(
        Guid intentId,
        TradingRuleSet rules,
        TradingBook book,
        decimal limit,
        RiskSignal signal,
        bool resting,
        CancellationToken cancellationToken,
        CapitalSnapshot? simulation = null
    )
    {
        var policy = rules.Adaptive ?? throw new InvalidOperationException("An adaptive trading policy is required.");
        var now = time.GetUtcNow();
        RiskAccountEvidence? observed;
        IReadOnlyList<TradingBook> quotes;
        lock (this.cacheGate)
        {
            observed =
                this.rulesHash == TradingRuleHash.Compute(rules) && this.evidence?.ObservedAt >= this.minimumObservedAt
                    ? this.evidence
                    : null;
            quotes = this.valuation;
        }

        var result =
            simulation is null && observed is not null
                ? await capital.Read(
                    book.Symbol,
                    null,
                    policy.Risk,
                    cancellationToken,
                    observed,
                    intentId,
                    quotes.Where(item => item.Symbol != book.Symbol).Append(book).ToArray()
                )
                : null;
        var snapshot =
            simulation
            ?? result?.Snapshot
            ?? new CapitalSnapshot(
                account.AccountKey,
                now,
                null,
                null,
                null,
                null,
                [],
                false,
                "Account refresh is pending; other markets and exits continue."
            );
        var bars = book.Bars.OrderBy(bar => bar.Timestamp).TakeLast(21).ToArray();
        var interval = TradingTimeframes.For(rules, book);
        var valid =
            TradingTimeframes.Supported(interval)
            && (rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3" || book.TimeframeSeconds == 300)
            && bars.Length == 21
            && bars[^1].Timestamp == TradingTimeframes.LastCompletedOpen(now, interval)
            && bars.All(bar =>
                bar.Low > 0
                && bar.High >= Math.Max(bar.Open, bar.Close)
                && bar.Low <= Math.Min(bar.Open, bar.Close)
                && bar.Volume >= 0
            )
            && bars.Zip(bars.Skip(1)).All(pair => pair.Second.Timestamp - pair.First.Timestamp == interval);
        var anchor = valid
            ? bars.TakeLast(20)
                .SingleOrDefault(bar => bar.Timestamp == signal.StopCandle && bar.Low == signal.StopPrice)
            : null;
        var atr = valid
            ? bars.Zip(bars.Skip(1))
                .Average(pair =>
                    decimal.Max(
                        pair.Second.High - pair.Second.Low,
                        decimal.Max(
                            decimal.Abs(pair.Second.High - pair.First.Close),
                            decimal.Abs(pair.Second.Low - pair.First.Close)
                        )
                    )
                )
            : 0;
        var market = new RiskMarketEvidence(
            book.Symbol,
            book.ObservedAt,
            book.Bid,
            book.Ask,
            book.AskQuantity * decimal.Min(book.Ask, limit),
            book.BidQuantity,
            atr,
            book.MinimumNotional,
            book.PriceStep,
            book.PriceScale,
            book.QuoteScale,
            book.Enabled,
            anchor is not null,
            resting,
            interval
        );
        var decision = AdaptiveRiskEngine.Evaluate(policy.Risk, snapshot, market, TradeSide.Buy, limit, signal, now);
        if (
            simulation is null
            && result is not null
            && (
                result.Evidence.OpenOrders.Any(order => order.Symbol == book.Symbol)
                || snapshot.Assets.Any(asset => asset.Symbol == book.Symbol && asset.CommittedBuyThb > 0)
            )
        )
        {
            decision = new(
                false,
                "existingOrder",
                null,
                null,
                null,
                null,
                null,
                null,
                "Reconcile the existing order for this market before another entry."
            );
        }

        var sizing = new RiskSizingSnapshot(
            Guid.NewGuid(),
            account.AccountKey,
            TradingRuleHash.Compute(rules),
            policy.Risk,
            snapshot,
            signal,
            market,
            decision,
            0
        );
        if (simulation is null)
        {
            await audit.RecordSizing(intentId, sizing, cancellationToken);
        }

        return sizing;
    }
}
