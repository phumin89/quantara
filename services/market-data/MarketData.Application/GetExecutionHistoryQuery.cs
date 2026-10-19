using Mediator;

namespace Quantara.MarketData.Application;

public sealed record GetExecutionHistoryQuery(string Symbol, int TimeframeSeconds, long From, long To) : IQuery<string>;
