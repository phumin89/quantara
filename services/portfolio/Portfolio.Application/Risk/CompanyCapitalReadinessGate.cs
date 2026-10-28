using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public static class CompanyCapitalReadinessGate
{
    public static CompanyCapitalReadiness Inspect(
        CompanyCapitalReport? report,
        IReadOnlyList<string> expectedAccountKeys,
        DateTimeOffset now,
        TimeSpan maximumAge
    )
    {
        if (maximumAge <= TimeSpan.Zero || maximumAge > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        var reasons = new List<string>();
        if (report is null)
        {
            return new(false, ["No reconciled capital interval is available."]);
        }

        var expected = expectedAccountKeys.ToHashSet(StringComparer.Ordinal);
        if (
            expected.Count == 0
            || expected.Count != expectedAccountKeys.Count
            || expected.Any(string.IsNullOrWhiteSpace)
            || !expected.SetEquals(report.AccountKeys)
            || report.AccountKeys.Count != expected.Count
        )
        {
            reasons.Add("Company account scope does not match the admitted evidence.");
        }

        if (
            report.EvidenceOrigin != "offlineFixture"
            || report.ImportRevision is null or <= 0
            || report.ImportDigests.Count == 0
            || report.ImportDigests.Any(digest => digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
        )
        {
            reasons.Add("Simulation requires admitted fixture provenance and retained import digests.");
        }

        if (report.To > now || now - report.To > maximumAge || report.From >= report.To)
        {
            reasons.Add("Closing capital evidence is stale, from the future or has invalid chronology.");
        }

        if (
            report.Issues.Count != 0
            || report.OpeningEquityThb is null or < 0
            || report.ClosingEquityThb is null or < 0
            || report.DepositsThb is null or < 0
            || report.WithdrawalsThb is null or < 0
            || report.NetContributionsThb is null
            || report.NetEquityChangeThb is null
            || report.InternalTransferNetThb is null
            || report.InternalTransferFeesThb is null or < 0
            || report.NetContributionsThb != report.DepositsThb - report.WithdrawalsThb
            || report.NetEquityChangeThb
                != report.ClosingEquityThb - report.OpeningEquityThb - report.NetContributionsThb
        )
        {
            reasons.Add("Valuation, coverage, classification or transfer-cost evidence is incomplete or inconsistent.");
        }

        return new(reasons.Count == 0, reasons);
    }
}
