namespace Quantara.Portfolio.Application;

public sealed record OrderBrokerResult(bool Accepted, string? OrderId, string? Message);
