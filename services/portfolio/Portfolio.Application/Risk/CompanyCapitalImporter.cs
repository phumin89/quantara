using System.Globalization;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed class CompanyCapitalImporter(
    RiskCapitalReader reader,
    IReadOnlyRiskStore risk,
    ICompanyCapitalEvidenceStore journal,
    IAccountContext account,
    TimeProvider time
)
{
    public async Task<CompanyCapitalImportResult> Import(
        CompanyBrokerAccount expectedSource,
        CancellationToken cancellationToken
    )
    {
        var origin = reader.Origin;
        var accountKey = account.AccountKey;
        if (
            expectedSource.AccountKey != accountKey
            || expectedSource.Broker != origin.Broker
            || expectedSource.LegalRegion != origin.LegalRegion
            || expectedSource.Environment != origin.Environment
            || origin
                is not {
                    Broker: "bitkub",
                    LegalRegion: "TH",
                    Environment: "offlineFixture",
                    AdapterVersion: "bitkub-risk-read-only-v1"
                }
        )
        {
            return Blocked("Source/account/region/environment is not admitted for this offline read-only importer.");
        }

        var retained = await journal.Read(cancellationToken);
        if (!retained.Accounts.Contains(expectedSource))
        {
            return Blocked("The source is outside the configured company evidence scope.");
        }

        var previous = await risk.ReadCapital(cancellationToken);
        if (
            previous is null
            || previous.AccountKey != accountKey
            || previous.Source != CapitalSource.Broker
            || previous.Blocker is not null
            || previous.HasUncertainOrders
            || previous.EquityThb is null
        )
        {
            return Blocked("A coherent retained broker opening valuation is required; no balance is fabricated.");
        }

        var policy = await risk.ReadPolicy(cancellationToken);
        var current = await reader.Read(null, null, policy.Policy, cancellationToken, valuationOnly: true);
        if (
            reader.Origin != origin
            || current.Origin != origin
            || account.AccountKey != accountKey
            || current.Snapshot.AccountKey != accountKey
        )
        {
            return Blocked("Source or account identity changed during collection.");
        }

        var now = time.GetUtcNow();
        if (
            current.Snapshot.ObservedAt <= previous.ObservedAt
            || current.Snapshot.ObservedAt > now
            || current.Markets.Any(book => book.ObservedAt > current.Snapshot.ObservedAt)
        )
        {
            return Blocked(
                "A newer coherent closing observation is required; later quotes cannot be relabeled at an earlier cutoff."
            );
        }

        var instant = current.Snapshot.ObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var importId = "risk-capital:" + instant;
        var period = new CompanyAccountPeriod(
            previous,
            current.Snapshot,
            current.Evidence.CashFlows,
            current.Evidence.CashFlowCoverageFrom,
            new Dictionary<string, string>()
        );
        var imported = await journal.Import(
            new(
                importId,
                expectedSource,
                "company-capital-fixture-v1",
                origin.AdapterVersion + ":" + instant,
                period,
                []
            ),
            retained.Revision,
            cancellationToken
        );
        var report = await journal.Report(previous.ObservedAt, current.Snapshot.ObservedAt, cancellationToken);
        var readiness = CompanyCapitalReadinessGate.Inspect(
            report,
            imported.Accounts.Select(source => source.AccountKey).ToArray(),
            time.GetUtcNow(),
            TimeSpan.FromSeconds(policy.Policy.MaximumDataAgeSeconds)
        );
        return new(importId, report?.ImportRevision ?? imported.Revision, report, readiness);
    }

    private static CompanyCapitalImportResult Blocked(string reason) => new(null, null, null, new(false, [reason]));
}
