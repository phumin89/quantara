using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed record OrderDiscoveryCase(
    string Name,
    string OpenOrders,
    IReadOnlyDictionary<string, string> History,
    IReadOnlyDictionary<string, string> OrderInfo,
    OrderState ExpectedState,
    string? ExpectedOrderId,
    int ExpectedInfoReads,
    int ExpectedHistoryReads,
    string? ExpectedMessage = null,
    string? ExpectedError = null
);
