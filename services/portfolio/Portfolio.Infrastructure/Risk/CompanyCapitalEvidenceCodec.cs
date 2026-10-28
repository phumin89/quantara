using System.Security.Cryptography;
using System.Text.Json;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Infrastructure.Risk;

internal static class CompanyCapitalEvidenceCodec
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static CompanyCapitalImport Normalize(
        CompanyCapitalImport import,
        IReadOnlyList<CompanyBrokerAccount> accounts,
        DateTimeOffset now
    )
    {
        if (
            !accounts.Contains(import.Source)
            || import.Source.Environment != "offlineFixture"
            || import.AdapterVersion != "company-capital-fixture-v1"
            || !Reference(import.Id)
            || !Reference(import.SourceReference)
            || import.Source.AccountKey != import.Period.Previous.AccountKey
            || import.Source.AccountKey != import.Period.Current.AccountKey
            || import.Period.Current.ObservedAt > now
            || import.Period.Flows.Count > 100000
            || import.Period.Flows.Any(flow => flow is null)
            || import.Transfers.Count > 100000
            || import.Transfers.Any(transfer => transfer is null)
        )
        {
            throw new InvalidDataException(
                "Capital import has an unsupported source, scope, protocol or evidence budget."
            );
        }

        var period = import.Period with
        {
            Previous = Snapshot(import.Period.Previous),
            Current = Snapshot(import.Period.Current),
            Flows = CapitalFlowEvidence
                .Unique(import.Period.Flows, import.Period.Current.ObservedAt)
                .Select(flow => flow with { At = flow.At.ToUniversalTime() })
                .ToArray(),
            CoverageFrom = import.Period.CoverageFrom.ToUniversalTime(),
            ExternalFlowEvidence = import
                .Period.ExternalFlowEvidence.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
        };
        _ = CompanyCapitalAccounting.Reconcile([period], [], [import.Source.AccountKey]);
        foreach (var transfer in import.Transfers)
        {
            if (
                transfer.AccountKey != import.Source.AccountKey
                || transfer.CounterpartyAccountKey == transfer.AccountKey
                || !accounts.Any(account => account.AccountKey == transfer.CounterpartyAccountKey)
                || !Reference(transfer.TransferReference)
                || !period.Flows.Any(flow => flow.Id == transfer.FlowId && flow.At > period.Previous.ObservedAt)
                || period.ExternalFlowEvidence.ContainsKey(transfer.FlowId)
            )
            {
                throw new InvalidDataException(
                    "Transfer provenance conflicts with its broker identity or capital classification."
                );
            }
        }

        return import with
        {
            Period = period,
            Transfers = import
                .Transfers.Distinct()
                .OrderBy(item => item.FlowId, StringComparer.Ordinal)
                .ThenBy(item => item.TransferReference, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    public static string Digest<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Options))).ToLowerInvariant();

    public static void Compatible(IReadOnlyList<CompanyCapitalImportReceipt> receipts)
    {
        var snapshots = new Dictionary<(string Account, DateTimeOffset At), string>();
        var flows = new Dictionary<(string Account, string Id), CapitalCashFlow>();
        var external = new Dictionary<(string Account, string Id), string>();
        var transfers = new Dictionary<(string Account, string Id), CompanyTransferObservation>();
        foreach (var receipt in receipts)
        {
            var import = receipt.Import;
            var account = import.Source.AccountKey;
            foreach (var snapshot in new[] { import.Period.Previous, import.Period.Current })
            {
                var key = (account, snapshot.ObservedAt);
                var digest = Digest(snapshot);
                if (!snapshots.TryAdd(key, digest) && snapshots[key] != digest)
                {
                    throw new InvalidDataException(
                        "A retained capital valuation cannot be replaced by another import."
                    );
                }
            }

            foreach (var flow in import.Period.Flows)
            {
                var key = (account, flow.Id);
                if (!flows.TryAdd(key, flow) && flows[key] != flow)
                {
                    throw new InvalidDataException("A retained broker movement cannot be replaced by another import.");
                }
            }

            foreach (var proof in import.Period.ExternalFlowEvidence)
            {
                var key = (account, proof.Key);
                if (transfers.ContainsKey(key) || !external.TryAdd(key, proof.Value) && external[key] != proof.Value)
                {
                    throw new InvalidDataException("Retained capital classification evidence conflicts.");
                }
            }

            foreach (var transfer in import.Transfers)
            {
                var key = (account, transfer.FlowId);
                if (external.ContainsKey(key) || !transfers.TryAdd(key, transfer) && transfers[key] != transfer)
                {
                    throw new InvalidDataException("Retained transfer classification evidence conflicts.");
                }
            }
        }
    }

    private static CapitalSnapshot Snapshot(CapitalSnapshot snapshot)
    {
        if (
            snapshot.Assets.Count > 1000
            || snapshot.Assets.Any(asset => asset is null)
            || snapshot.Assets.Select(asset => asset.Symbol).Distinct(StringComparer.Ordinal).Count()
                != snapshot.Assets.Count
            || snapshot.Assets.Any(asset =>
                string.IsNullOrWhiteSpace(asset.Symbol)
                || asset.Quantity < 0
                || asset.PriceThb < 0
                || asset.Available < 0
                || asset.Available > asset.Quantity
            )
            || snapshot.EquityThb < 0
            || snapshot.EquityThb is { } equity
                && (
                    snapshot.Assets.Any(asset => asset.Quantity > 0 && asset.PriceThb is null or <= 0)
                    || snapshot.Assets.Sum(asset => asset.Quantity * asset.PriceThb.GetValueOrDefault()) != equity
                )
        )
        {
            throw new InvalidDataException("Capital import has inconsistent asset evidence.");
        }

        return snapshot with
        {
            ObservedAt = snapshot.ObservedAt.ToUniversalTime(),
            Assets = snapshot.Assets.OrderBy(asset => asset.Symbol, StringComparer.Ordinal).ToArray(),
        };
    }

    private static bool Reference(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(RiskJson.Options)
        {
            PropertyNameCaseInsensitive = false,
            MaxDepth = 32,
        };
        options.Converters.Add(new CompanyDecimalJsonConverter());
        options.Converters.Add(new CompanyInstantJsonConverter());
        return options;
    }
}
