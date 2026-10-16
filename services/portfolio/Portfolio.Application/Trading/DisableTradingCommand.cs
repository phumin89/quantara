using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record DisableTradingCommand(bool Emergency) : ICommand<string>;
