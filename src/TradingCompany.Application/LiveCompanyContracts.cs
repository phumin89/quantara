using TradingCompany.Domain;

namespace TradingCompany.Application;

// Advisory process receives no broker credential, activation authority or storage capability.
public sealed record LiveResearchLimits(decimal CapitalAllocation, decimal MaximumPositionWeight,
    int MaximumOpenPositions, decimal FeeReserveRate, decimal MaximumSlippage, IReadOnlyList<string> AllowedSymbols);
public sealed record LiveResearchPacket(TradingAccountContext AccountContext, long MandateVersion,
    LiveSnapshot Snapshot, LiveResearchLimits Limits, bool Armed, bool Stopped);
public sealed record LiveIntentReceipt(LiveIntent Intent, LiveOrderState State, IReadOnlyList<string> Violations);
public interface ILiveCompanyClient
{
    Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken);
    Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken);
}
