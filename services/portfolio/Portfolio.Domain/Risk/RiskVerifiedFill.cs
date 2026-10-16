namespace Quantara.Portfolio.Domain.Risk;

public sealed record RiskVerifiedFill(
    string TransactionId,
    decimal Quantity,
    decimal Price,
    decimal FeeThb,
    decimal FeeCreditThb,
    DateTimeOffset At
);
