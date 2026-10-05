using System.Text.Json;
using TradingCompany.Application;

namespace TradingCompany.Rehearsal;

public static class RehearsalVerification
{
    public static bool Passed(ForwardPaperAccount before, ForwardPaperAccount after,
        bool fundingReconciled, string serviceStatus, int callsBefore, int callsAfter)
    {
        if (!fundingReconciled || serviceStatus is not ("COMPLETED" or "AWAITING_EXECUTION_QUOTE") ||
            callsBefore != callsAfter || before.FundingAccountId != after.FundingAccountId ||
            before.Events.Count > after.Events.Count) return false;
        try
        {
            ForwardPaperLedger.Validate(before);
            ForwardPaperLedger.Validate(after);
            return before.Events.Select(e => JsonSerializer.Serialize(e))
                .SequenceEqual(after.Events.Take(before.Events.Count).Select(e => JsonSerializer.Serialize(e)));
        }
        catch (InvalidDataException) { return false; }
    }
}
