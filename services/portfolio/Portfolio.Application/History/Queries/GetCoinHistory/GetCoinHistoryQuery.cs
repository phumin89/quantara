using Mediator;
namespace Quantara.Portfolio.Application;

public sealed record GetCoinHistoryQuery(string Coin) : IQuery<CoinHistory>;
