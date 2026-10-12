using Mediator;
using Quantara.Api.Domain;

namespace Quantara.Api.Application;

public interface IPortfolioStore
{
    Task<PortfolioLedger> Load(CancellationToken cancellationToken);
    Task Save(PortfolioLedger ledger, CancellationToken cancellationToken);
}

public interface IPortfolioSync
{
    SyncStatus Status { get; }
    bool Request();
}

public sealed record SyncStatus(bool Syncing, int CompletedSymbols, int TotalSymbols, string? Error);
public sealed record PortfolioView(Position[] Positions, PortfolioSummary Summary, DateTimeOffset? UpdatedAt,
    DateTimeOffset? PriceUpdatedAt, DateTimeOffset? HistoryUpdatedAt, string HistoryCoverage,
    bool Syncing, int CompletedSymbols, int TotalSymbols, string? Error);
public sealed record GetPortfolio : IQuery<PortfolioView>;
public sealed record RefreshPortfolio : ICommand<bool>;
public sealed record GetCoinHistory(string Coin) : IQuery<CoinHistory>;
public sealed record CoinHistory(Trade[] Trades, string Coverage);

public static class HistoryCoverage
{
    public const string Notice = "Bitkub exposes recent exchange trade history (90 days). Earlier activity and broker conversions may be missing. Cost and profit remain unknown until a complete ledger is verified. Imported records are retained.";
}

public sealed class GetPortfolioHandler(IPortfolioStore store, IPortfolioSync sync) : IQueryHandler<GetPortfolio, PortfolioView>
{
    public async ValueTask<PortfolioView> Handle(GetPortfolio query, CancellationToken cancellationToken)
    {
        var ledger = await store.Load(cancellationToken);
        var status = sync.Status;
        return new(PortfolioProjection.Positions(ledger), PortfolioProjection.Summary(ledger), ledger.UpdatedAt, ledger.PriceUpdatedAt,
            ledger.HistoryUpdatedAt, HistoryCoverage.Notice + (ledger.UnavailableHistorySymbols.Length > 0 ? $" History unavailable for {ledger.UnavailableHistorySymbols.Length} exchange markets." : ""), status.Syncing,
            status.CompletedSymbols, status.TotalSymbols, status.Error);
    }
}

public sealed class RefreshPortfolioHandler(IPortfolioSync sync) : ICommandHandler<RefreshPortfolio, bool>
{
    public ValueTask<bool> Handle(RefreshPortfolio command, CancellationToken cancellationToken) => new(sync.Request());
}

public sealed class GetCoinHistoryHandler(IPortfolioStore store) : IQueryHandler<GetCoinHistory, CoinHistory>
{
    public async ValueTask<CoinHistory> Handle(GetCoinHistory query, CancellationToken cancellationToken)
    {
        var ledger = await store.Load(cancellationToken);
        return new(ledger.Trades.Where(trade => trade.Symbol == query.Coin)
            .OrderByDescending(trade => trade.Timestamp).ToArray(), HistoryCoverage.Notice);
    }
}
