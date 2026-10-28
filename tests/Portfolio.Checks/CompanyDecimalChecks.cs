using System.Globalization;
using System.Text;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Infrastructure.Risk;

namespace Portfolio.Checks;

public static class CompanyDecimalChecks
{
    public static async Task Run(string path)
    {
        var account = CompanySimulationFixture.Account;
        var import = CompanySimulationFixture.Import(
            "decimal-precision",
            account,
            CompanySimulationFixture.At,
            CompanySimulationFixture.At.AddDays(1),
            1000,
            1000
        );
        var store = new CompanyCapitalEvidenceStore(path, [account]);
        var imported = await store.Import(import, 0, CancellationToken.None);
        var receipt = imported.Receipts.Single();
        var canonical = await File.ReadAllTextAsync(path);

        foreach (var amount in new[] { "1000", "1000.00", "1e3", "1.0000000000000000000000000000000000e3" })
        {
            await Accept(path, ReplaceCurrent(canonical, "Available", "1000", amount), import, receipt.Digest);
        }

        foreach (var amount in new[] { "0", "-0", "0e-29" })
        {
            await Accept(path, ReplaceCurrent(canonical, "ReservedThb", "0", amount), import, receipt.Digest);
        }

        foreach (
            var amount in new[]
            {
                "1000." + new string('0', 28) + "1",
                "100000000000000000000000000000001e-29",
                "\"1000\"",
                "1e100",
            }
        )
        {
            await Reject(path, ReplaceCurrent(canonical, "Available", "1000", amount));
        }

        foreach (var amount in new[] { "0." + new string('0', 28) + "1", "1e-29", "-1e-29" })
        {
            await Reject(path, ReplaceCurrent(canonical, "ReservedThb", "0", amount));
        }

        // A nonzero scale-28 value must survive the JSON converter, not just the broker reader.
        var precisePath = path + ".scale";
        var preciseStore = new CompanyCapitalEvidenceStore(precisePath, [account]);
        var current = import.Period.Current;
        var precise = import with
        {
            Period = import.Period with
            {
                Current = current with
                {
                    Assets = [current.Assets.Single() with { Available = 0.0000000000000000000000000001m }],
                },
            },
        };
        var preciseImport = await preciseStore.Import(precise, 0, CancellationToken.None);
        var preciseReceipt = preciseImport.Receipts.Single();
        var preciseJson = await File.ReadAllTextAsync(precisePath);
        var nativeToken = 0.0000000000000000000000000001m.ToString("G29", CultureInfo.InvariantCulture);
        await Accept(precisePath, preciseJson, precise, preciseReceipt.Digest);
        await Accept(
            precisePath,
            ReplaceCurrent(preciseJson, "Available", nativeToken, "0.00000000000000000000000000010"),
            precise,
            preciseReceipt.Digest
        );
        Console.WriteLine(
            "Company decimal evidence: exact equivalents and scale-28 values retained; rounded and quoted amounts rejected without rewriting evidence."
        );
    }

    private static string ReplaceCurrent(string journal, string field, string original, string replacement)
    {
        var current = journal.IndexOf("\"Current\":", StringComparison.Ordinal);
        Check(current >= 0, "The fixture must contain a current observation.");
        var marker = $"\"{field}\":{original},";
        var offset = journal.IndexOf(marker, current, StringComparison.Ordinal);
        Check(offset >= 0, "The exact current-observation number token must exist before replacement.");
        return journal[..offset] + $"\"{field}\":{replacement}," + journal[(offset + marker.Length)..];
    }

    private static async Task Accept(string path, string payload, CompanyCapitalImport import, string digest)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await File.WriteAllBytesAsync(path, bytes);
        var store = new CompanyCapitalEvidenceStore(path, [CompanySimulationFixture.Account]);
        var journal = await store.Read(CancellationToken.None);
        Check(
            journal.Revision == 1 && journal.Receipts.Count == 1,
            "Exact alternate numeric forms retain the journal revision."
        );
        var receipt = journal.Receipts.Single();
        Check(receipt.Digest == digest, "Exact numeric forms retain the content digest.");
        Check(
            receipt.Import.Period.Current.Assets.Single().Available == import.Period.Current.Assets.Single().Available
                && receipt.Import.Period.Current.ReservedThb == import.Period.Current.ReservedThb,
            "Exact current amounts survive native JSON parsing."
        );
        var replay = await store.Import(import, 0, CancellationToken.None);
        Check(
            replay.Receipts.Single().Digest == digest,
            "A restart replay remains idempotent despite numeric formatting."
        );
        var retained = await File.ReadAllBytesAsync(path);
        Check(bytes.SequenceEqual(retained), "Valid evidence is not rewritten by read or replay.");
    }

    private static async Task Reject(string path, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await File.WriteAllBytesAsync(path, bytes);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var rejected = false;
            try
            {
                await new CompanyCapitalEvidenceStore(path, [CompanySimulationFixture.Account]).Read(
                    CancellationToken.None
                );
            }
            catch (InvalidDataException error)
                when (error.Message == "Capital evidence journal is malformed; retained evidence was not reset.")
            {
                rejected = true;
            }

            Check(rejected, "Inexact, overflowed or quoted numbers must fail at the decimal evidence boundary.");
            var retained = await File.ReadAllBytesAsync(path);
            Check(bytes.SequenceEqual(retained), "Malformed evidence survives restart reads byte-for-byte.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
