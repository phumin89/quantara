namespace Quantara.Portfolio.Domain;

public sealed record ManualSaleLot(
    string AccountKey,
    string LotKey,
    string Symbol,
    DateTimeOffset OpenedAt,
    decimal RecordedQuantity,
    decimal RecordedCostThb,
    decimal? BrokerCreditedQuantity = null
);
