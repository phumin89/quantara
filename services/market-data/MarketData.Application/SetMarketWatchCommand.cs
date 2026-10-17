using Mediator;

namespace Quantara.MarketData.Application;

public sealed record SetMarketWatchCommand(IReadOnlyList<string> Symbols) : ICommand<bool>;
