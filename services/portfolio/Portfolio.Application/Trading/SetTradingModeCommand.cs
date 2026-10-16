using Mediator;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed record SetTradingModeCommand(TradingMode Mode) : ICommand<string>;
