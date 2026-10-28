namespace Quantara.Portfolio.Application;

public sealed record CashBaselineReviewResult(
    string AccountKey,
    decimal CashThb,
    decimal AvailableThb,
    DateTimeOffset BrokerObservedAt,
    DateTimeOffset ExpiresAt,
    string Proof,
    string Acknowledgement,
    bool Applied,
    bool CurrentDayProfitUnknown
);
