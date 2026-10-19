using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record PrepareAdaptiveTradingCommand(string AccountKey, string RulesHash) : ICommand<string>;
