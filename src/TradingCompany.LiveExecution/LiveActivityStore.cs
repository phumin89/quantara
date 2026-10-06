using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.LiveExecution;

// One bounded, ephemeral snapshot. Trading state and saved meeting reports stay in the ledger.
public sealed class LiveActivityStore(TimeProvider clock)
{
    private readonly object gate = new();
    private LiveCompanyActivity? current;

    public bool Save(LiveCompanyActivity activity, LiveAccountId account)
    {
        if (!LiveActivityValidation.IsValid(activity, account, clock.GetUtcNow())) return false;
        lock (gate)
        {
            if (current is not null && (current.AccountContext != activity.AccountContext || current.ObservedAt >= activity.ObservedAt)) return false;
            current = activity with { Roles = activity.Roles.ToArray() };
            return true;
        }
    }

    public LiveCompanyActivity? Read(LiveAccountId account)
    {
        lock (gate)
            return current is not null && LiveActivityValidation.IsValid(current, account, clock.GetUtcNow())
                ? current with { Roles = current.Roles.ToArray() } : null;
    }
}
