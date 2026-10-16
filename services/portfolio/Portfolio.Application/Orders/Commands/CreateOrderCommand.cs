using Mediator;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record CreateOrderCommand(
    string Symbol,
    TradeSide Side,
    string Amount,
    string LimitPrice,
    string Reason,
    Guid? SourceRunId = null,
    string? ExpectedAccountKey = null,
    Quantara.Portfolio.Domain.Risk.RiskSignal? RiskSignal = null
) : ICommand<OrderView>;
