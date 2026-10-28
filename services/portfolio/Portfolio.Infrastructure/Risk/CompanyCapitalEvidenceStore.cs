using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Infrastructure.Risk;

public sealed class CompanyCapitalEvidenceStore : ICompanyCapitalEvidenceStore
{
    private const int maximumJournalBytes = 32 * 1024 * 1024;

    private readonly string path;

    private readonly CompanyBrokerAccount[] accounts;

    public CompanyCapitalEvidenceStore(string path, IReadOnlyList<CompanyBrokerAccount> accounts)
    {
        if (
            !Path.IsPathFullyQualified(path)
            || accounts.Count is < 1 or > 100
            || accounts.Select(account => account.AccountKey).Distinct(StringComparer.Ordinal).Count() != accounts.Count
            || accounts.Any(account =>
                string.IsNullOrWhiteSpace(account.AccountKey)
                || string.IsNullOrWhiteSpace(account.Broker)
                || string.IsNullOrWhiteSpace(account.LegalRegion)
                || account.Environment != "offlineFixture"
            )
        )
        {
            throw new InvalidDataException(
                "Offline capital journal requires an absolute path and explicit fixture account scope."
            );
        }

        this.path = Path.GetFullPath(path);
        this.accounts = accounts.OrderBy(account => account.AccountKey, StringComparer.Ordinal).ToArray();
    }

    public async Task<CompanyCapitalEvidenceState> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream input;
        try
        {
            input = new FileStream(
                this.path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous
            );
        }
        catch (FileNotFoundException)
        {
            return new(1, 0, this.accounts.ToArray(), []);
        }
        catch (DirectoryNotFoundException)
        {
            return new(1, 0, this.accounts.ToArray(), []);
        }

        await using var stream = input;
        if (stream.Length > maximumJournalBytes)
        {
            throw new InvalidDataException("Capital evidence journal exceeded its read budget.");
        }

        CompanyCapitalEvidenceState state;
        try
        {
            state =
                await JsonSerializer.DeserializeAsync<CompanyCapitalEvidenceState>(
                    stream,
                    CompanyCapitalEvidenceCodec.Options,
                    cancellationToken
                ) ?? throw new InvalidDataException("Capital evidence journal is empty.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Capital evidence journal is malformed; retained evidence was not reset.");
        }

        if (
            state.FormatVersion != 1
            || state.Revision != state.Receipts.Count
            || state.Receipts.Count > 10000
            || !state.Accounts.SequenceEqual(this.accounts)
            || state.Receipts.Any(receipt =>
                receipt is null
                || receipt.Import is null
                || receipt.Import.Source is null
                || receipt.Import.Period is null
            )
            || state
                .Receipts.Select(receipt => (receipt.Import.Source.AccountKey, receipt.Import.Id))
                .Distinct()
                .Count() != state.Receipts.Count
        )
        {
            throw new InvalidDataException("Capital evidence journal has invalid version, scope or receipt identity.");
        }

        foreach (var receipt in state.Receipts)
        {
            var normalized = CompanyCapitalEvidenceCodec.Normalize(
                receipt.Import,
                this.accounts,
                DateTimeOffset.UtcNow
            );
            if (
                receipt.Digest != CompanyCapitalEvidenceCodec.Digest(normalized)
                || receipt.RecordedAt < normalized.Period.Current.ObservedAt
            )
            {
                throw new InvalidDataException("Capital evidence receipt does not match its retained content.");
            }
        }

        CompanyCapitalEvidenceCodec.Compatible(state.Receipts);
        return state;
    }

    public async Task<CompanyCapitalEvidenceState> Import(
        CompanyCapitalImport import,
        long expectedRevision,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = CompanyCapitalEvidenceCodec.Normalize(import, this.accounts, DateTimeOffset.UtcNow);
        var digest = CompanyCapitalEvidenceCodec.Digest(normalized);
        var directory =
            Path.GetDirectoryName(this.path) ?? throw new InvalidDataException("Journal parent is unavailable.");
        Directory.CreateDirectory(directory);
        await using var ownership = new FileStream(
            this.path + ".lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        var current = await this.Read(cancellationToken);
        var existing = current.Receipts.SingleOrDefault(receipt =>
            receipt.Import.Source.AccountKey == normalized.Source.AccountKey && receipt.Import.Id == normalized.Id
        );
        if (existing is not null)
        {
            if (existing.Digest != digest)
            {
                throw new InvalidDataException("Capital import identity already belongs to different evidence.");
            }

            return current;
        }

        if (current.Revision != expectedRevision)
        {
            throw new InvalidOperationException("Capital journal revision changed; reread before appending evidence.");
        }

        var next = current with
        {
            Revision = current.Revision + 1,
            Receipts = [.. current.Receipts, new(normalized, digest, DateTimeOffset.UtcNow)],
        };
        CompanyCapitalEvidenceCodec.Compatible(next.Receipts);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next, CompanyCapitalEvidenceCodec.Options);
        if (bytes.Length > maximumJournalBytes || next.Receipts.Count > 10000)
        {
            throw new InvalidDataException("Capital evidence journal exceeded its append budget.");
        }

        var temporary = this.path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (
                var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous
                )
            )
            {
                await output.WriteAsync(bytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, this.path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return next;
    }

    public async Task<CompanyCapitalReport?> Report(
        DateTimeOffset observedFrom,
        DateTimeOffset observedTo,
        CancellationToken cancellationToken
    )
    {
        if (observedTo <= observedFrom)
        {
            throw new InvalidDataException("Capital report interval is invalid.");
        }

        var state = await this.Read(cancellationToken);
        var receipts = state
            .Receipts.Where(receipt =>
                receipt.Import.Period.Previous.ObservedAt == observedFrom
                && receipt.Import.Period.Current.ObservedAt == observedTo
            )
            .ToArray();
        var imports = receipts.Select(receipt => receipt.Import).ToArray();
        if (imports.Length == 0)
        {
            return null;
        }

        var periods = imports
            .GroupBy(import => import.Source.AccountKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First().Period;
                return first with
                {
                    Flows = CapitalFlowEvidence.Unique(
                        group.SelectMany(import => import.Period.Flows).ToArray(),
                        observedTo
                    ),
                    CoverageFrom = group.Min(import => import.Period.CoverageFrom),
                    ExternalFlowEvidence = group
                        .SelectMany(import => import.Period.ExternalFlowEvidence)
                        .Distinct()
                        .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                };
            })
            .ToArray();
        var observations = imports.SelectMany(import => import.Transfers).Distinct().ToArray();
        var links = CompanyTransferClassifier.Match(periods, observations);
        return CompanyCapitalAccounting.Reconcile(
            periods,
            links,
            this.accounts.Select(account => account.AccountKey).ToArray()
        ) with
        {
            EvidenceOrigin = "offlineFixture",
            ImportRevision = state.Revision,
            ImportDigests = receipts
                .Select(receipt => receipt.Digest)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
        };
    }
}
