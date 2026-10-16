using Mediator;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed record ConfigureTradingCommand(TradingRuleSet Rules) : ICommand<string>;
