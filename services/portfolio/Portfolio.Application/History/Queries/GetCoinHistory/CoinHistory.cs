using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record CoinHistory(Trade[] Trades, string Coverage);
