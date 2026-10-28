using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class RiskCapitalReader(
    IRiskEvidenceReader reader,
    IReadOnlyOrderStore orders,
    IReadOnlyTradingStore trading,
    ITradingMarketReader markets,
    IAccountContext account,
    TimeProvider time
)
{
    public RiskEvidenceOrigin Origin => reader.Origin;

    public async Task<CapitalReadResult> Read(
        string? candidate,
        Guid? excludeProposal,
        RiskPolicy policy,
        CancellationToken cancellationToken,
        RiskAccountEvidence? observed = null,
        Guid? excludeEngineIntent = null,
        IReadOnlyList<TradingBook>? observedMarkets = null,
        bool valuationOnly = false
    )
    {
        var evidence = observed ?? await reader.Read(cancellationToken);
        var now = time.GetUtcNow();
        var local = (await orders.Exposure(now, cancellationToken))
            .Where(order => order.Id != excludeProposal)
            .ToArray();
        var autonomous = (await trading.Read(cancellationToken))
            .Live.Orders.Where(order => order.Id != excludeEngineIntent)
            .ToArray();
        var pending = local.Where(order => order.State == OrderState.Pending && order.ExpiresAt > now).ToArray();
        var pendingEngine = autonomous.Where(order => order.Status == "pending").ToArray();
        var balances = evidence.Balances;
        if (
            balances.Any(balance =>
                string.IsNullOrWhiteSpace(balance.Symbol)
                || balance.Quantity < 0
                || balance.Available is null
                || balance.Available < 0
                || balance.Available > balance.Quantity
            )
            || balances.Select(balance => balance.Symbol).Distinct(StringComparer.Ordinal).Count() != balances.Length
        )
        {
            throw new InvalidDataException("Fresh account balances are incomplete or inconsistent.");
        }

        var symbols = balances
            .Where(balance => balance.Symbol != "THB" && balance.Quantity > 0)
            .Select(balance => balance.Symbol)
            .Concat(evidence.OpenOrders.Select(order => order.Symbol))
            .Concat(pending.Select(order => order.Symbol))
            .Concat(pendingEngine.Select(order => order.Symbol))
            .Concat(candidate is null ? [] : new[] { candidate })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var books = new List<TradingBook>();
        if (observedMarkets is not null)
        {
            books.AddRange(observedMarkets.Where(book => symbols.Contains(book.Symbol, StringComparer.Ordinal)));
        }
        else
        {
            foreach (var chunk in symbols.Chunk(16))
            {
                books.AddRange(
                    valuationOnly
                        ? await markets.ReadValuations(chunk, cancellationToken)
                        : await markets.Read(chunk, cancellationToken)
                );
            }
        }

        if (
            books.Select(book => book.Symbol).Distinct(StringComparer.Ordinal).Count() != books.Count
            || books.Any(book => !symbols.Contains(book.Symbol, StringComparer.Ordinal))
        )
        {
            throw new InvalidDataException("Market valuation identities are inconsistent.");
        }

        now = time.GetUtcNow();
        var cash = balances.SingleOrDefault(balance => balance.Symbol == "THB");
        var pendingCash =
            pending.Where(order => order.Side == TradeSide.Buy).Sum(order => order.Amount + order.EstimatedFeeThb)
            + pendingEngine
                .Where(order => order.Side == TradeSide.Buy)
                .Sum(order => order.Amount * (1 + policy.EstimatedFeeRate));
        var reserved = cash?.Available is { } available ? cash.Quantity - available : (decimal?)null;
        var brokerBuy = evidence.OpenOrders.Where(order => order.Side == TradeSide.Buy).Sum(order => order.Remaining);
        var pendingBuy =
            pending.Where(order => order.Side == TradeSide.Buy).Sum(order => order.Amount)
            + pendingEngine.Where(order => order.Side == TradeSide.Buy).Sum(order => order.Amount);
        var uncertain =
            local.Any(order => order.State is OrderState.Submitting or OrderState.Unknown)
            || autonomous.Any(order =>
                order.Status is "submitting" or "unknown" || order.CancellationState is "claimed" or "unknown"
            )
            || autonomous.Any(order =>
                order.Status is "submitted" or "partial"
                && !evidence.OpenOrders.Any(open =>
                    open.Id == order.BrokerOrderId && open.Symbol == order.Symbol && open.Side == order.Side
                )
            )
            || local.Any(order =>
                order.State is OrderState.Submitted or OrderState.PartiallyFilled
                && !evidence.OpenOrders.Any(open =>
                    open.Id == order.BrokerOrderId && open.Symbol == order.Symbol && open.Side == order.Side
                )
            );
        var assets = symbols
            .Select(symbol =>
            {
                var balance = balances.SingleOrDefault(item => item.Symbol == symbol);
                var book = books.SingleOrDefault(item => item.Symbol == symbol);
                var price =
                    book is not null
                    && book.Bid > 0
                    && book.ObservedAt <= now.AddSeconds(5)
                    && now - book.ObservedAt <= TimeSpan.FromSeconds(policy.MaximumDataAgeSeconds)
                        ? book.Bid
                        : (decimal?)null;
                var pendingSell =
                    pending
                        .Where(order => order.Symbol == symbol && order.Side == TradeSide.Sell)
                        .Sum(order => order.Amount)
                    + pendingEngine
                        .Where(order => order.Symbol == symbol && order.Side == TradeSide.Sell)
                        .Sum(order => order.Amount);
                var committedBuy =
                    evidence
                        .OpenOrders.Where(order => order.Symbol == symbol && order.Side == TradeSide.Buy)
                        .Sum(order => order.Remaining)
                    + pending
                        .Where(order => order.Symbol == symbol && order.Side == TradeSide.Buy)
                        .Sum(order => order.Amount)
                    + pendingEngine
                        .Where(order => order.Symbol == symbol && order.Side == TradeSide.Buy)
                        .Sum(order => order.Amount);
                return new CapitalAsset(
                    symbol,
                    balance?.Quantity ?? 0,
                    balance?.Available ?? 0,
                    price,
                    pendingSell,
                    committedBuy
                );
            })
            .ToArray();
        string? blocker = null;
        if (cash?.Available is null)
        {
            blocker = "Available THB is Unknown.";
        }
        else if (assets.Any(asset => asset.Quantity > 0 && asset.PriceThb is null))
        {
            blocker = "Some held assets have no fresh verified THB price; total equity is Unknown.";
        }
        else if (
            now - evidence.ObservedAt > TimeSpan.FromSeconds(policy.MaximumDataAgeSeconds)
            || evidence.ObservedAt > now.AddSeconds(5)
        )
        {
            blocker = "Account evidence became stale during collection.";
        }
        else if (reserved < brokerBuy)
        {
            blocker = "Open orders and reserved THB changed during collection. Refresh a coherent snapshot.";
        }

        decimal? equity =
            cash is not null && !assets.Any(asset => asset.Quantity > 0 && asset.PriceThb is null)
                ? cash.Quantity + assets.Sum(asset => asset.Quantity * (asset.PriceThb ?? 0))
                : null;
        decimal? exposure =
            equity is not null && reserved is not null
                ? assets.Sum(asset => asset.Quantity * (asset.PriceThb ?? 0))
                    + decimal.Max(reserved.Value, brokerBuy)
                    + pendingBuy
                : null;
        var walletAssets = cash is null
            ? assets
            : assets.Append(new CapitalAsset("THB", cash.Quantity, cash.Available, 1, 0)).ToArray();
        var snapshot = new CapitalSnapshot(
            account.AccountKey,
            evidence.ObservedAt,
            cash?.Available is { } spendable ? decimal.Max(0, spendable - pendingCash) : null,
            equity,
            exposure,
            reserved,
            walletAssets,
            uncertain,
            blocker
        )
        {
            Source = CapitalSource.Broker,
        };
        return new(snapshot, evidence, books) { Origin = reader.Origin };
    }
}
