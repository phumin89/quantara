using TradingCompany.Domain;

namespace TradingCompany.Application;

// Advisory process receives no broker credential, activation authority or storage capability.
public sealed record LiveOrderSizing(string Symbol, decimal PriceStep, decimal BaseStep, decimal QuoteStep, decimal MinimumQuoteSize);
public sealed record LiveResearchLimits(decimal CapitalAllocation, decimal MaximumPositionWeight,
    int MaximumOpenPositions, decimal FeeReserveRate, decimal MaximumSlippage, IReadOnlyList<string> AllowedSymbols)
{
    public IReadOnlyList<LiveOrderSizing> OrderSizing { get; init; } = [];
}
public sealed record LiveResearchPacket(TradingAccountContext AccountContext, long MandateVersion,
    LiveSnapshot Snapshot, LiveResearchLimits Limits, bool Armed, bool Stopped);
public sealed record LiveIntentReceipt(LiveIntent Intent, LiveOrderState State, IReadOnlyList<string> Violations);
public interface ILiveCompanyClient
{
    Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken);
    Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken);
    Task SaveMeetingAsync(DailyMeetingAttempt attempt, CancellationToken cancellationToken);
}
