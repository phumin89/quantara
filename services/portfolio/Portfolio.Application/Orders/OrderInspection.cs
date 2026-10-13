using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record OrderInspection(OrderState State, string? BrokerOrderId, string? Message);
