using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;
using Quantara.Portfolio.Infrastructure.Risk;

namespace Portfolio.Checks;

public static class TradingPersistenceSqlChecks
{
    public static async Task Run()
    {
        var configured = Environment.GetEnvironmentVariable("QUANTARA_FEATURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Console.WriteLine("Trading persistence SQL checks skipped (isolated LocalDB opt-in is not configured).");
            return;
        }

        var settings = new SqlConnectionStringBuilder(configured);
        if (
            !settings.InitialCatalog.StartsWith("QuantaraFeatureChecks", StringComparison.Ordinal)
            || !settings.DataSource.StartsWith("(localdb)\\QuantaraVerify", StringComparison.Ordinal)
            || !settings.IntegratedSecurity
        )
        {
            throw new InvalidOperationException(
                "Persistence checks require a separate QuantaraVerify LocalDB fixture."
            );
        }

        await using var connection = new SqlConnection(configured);
        await connection.OpenAsync();
        await using var local = new SqlCommand("SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB'));", connection);
        Check((int)(await local.ExecuteScalarAsync() ?? 0) == 1, "Fixture must be LocalDB, never production SQL.");
        var account = Guid.NewGuid().ToString("N") + new string('a', 32);
        var otherAccount = new string('b', 64);
        var now = DateTimeOffset.UtcNow;
        var store = new SqlTradingStore(configured, account, new FixedTimeProvider(now));
        await store.Initialize(CancellationToken.None);
        var initial = await store.Read(CancellationToken.None);
        Check(initial.Mode == TradingMode.Paper && initial.Mandate is null, "Fresh SQL never grants live authority.");
        var fill = new TradingFill(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "fixture-fill",
            "BTC",
            TradeSide.Buy,
            .1m,
            1000m,
            100m,
            .25m,
            now,
            GrossFeeThb: .30m,
            FeeCreditThb: .05m
        );
        var paper = new CapitalSnapshot(account, now, 900m, 1000m, 100m, 0m, [], false, null)
        {
            Source = CapitalSource.Paper,
        };
        var saved = initial with
        {
            Revision = initial.Revision + 1,
            Mode = TradingMode.Stopped,
            Paper = initial.Paper with { FundingThb = 1000, CashThb = 899.75m, Fills = [fill] },
            Autonomy = new(now, null, 0, 0, [], paper, "fixture", now.AddMinutes(1)),
        };
        Check(await store.Save(saved, initial.Revision, CancellationToken.None), "Initial fixture state commits.");
        Check(
            !await store.Save(saved, initial.Revision, CancellationToken.None),
            "Stale revision fails CAS without a write."
        );
        var restarted = new SqlTradingStore(configured, account, new FixedTimeProvider(now));
        await restarted.Initialize(CancellationToken.None);
        var restored = await restarted.Read(CancellationToken.None);
        Check(
            restored.Mode == TradingMode.Stopped
                && restored.Mandate is null
                && restored.Paper.Fills.Single() == fill
                && restored.Autonomy?.Capital?.Source == CapitalSource.Paper,
            "Stop, exact net fees and provenance survive SQL store restart."
        );
        var other = new SqlTradingStore(configured, otherAccount, new FixedTimeProvider(now));
        await other.Initialize(CancellationToken.None);
        Check(
            (await other.Read(CancellationToken.None)).Paper.Fills.Count == 0,
            "SQL account partition hides fixture fills."
        );

        var changed = restored with
        {
            Revision = restored.Revision + 1,
            Paper = restored.Paper with { Fills = [fill with { FeeThb = .250m }] },
        };
        try
        {
            await restarted.Save(changed, restored.Revision, CancellationToken.None);
            throw new InvalidOperationException("Changed immutable fee representation was accepted.");
        }
        catch (InvalidDataException)
        {
            Check(
                (await restarted.Read(CancellationToken.None)).Revision == restored.Revision,
                "An immutable-history failure rolls back the runtime revision."
            );
        }

        await using var legacy = new SqlCommand(
            "UPDATE dbo.TradingRuntime SET Payload=JSON_MODIFY(Payload,'$.autonomy.capital.source',NULL) WHERE AccountKey=@account;",
            connection
        );
        legacy.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        await legacy.ExecuteNonQueryAsync();
        Check(
            (await restarted.Read(CancellationToken.None)).Autonomy?.Capital?.Source == CapitalSource.Unknown,
            "Legacy trading JSON without provenance reads Unknown, never Broker."
        );

        var risk = new SqlRiskStore(configured, account);
        await risk.Initialize(CancellationToken.None);
        await risk.Observe(paper with { Source = CapitalSource.Broker }, [], now.AddDays(-1), CancellationToken.None);
        Check(
            (await new SqlRiskStore(configured, account).ReadCapital(CancellationToken.None))?.Source
                == CapitalSource.Broker,
            "Broker provenance round-trips in independent risk storage."
        );
        Check(
            await new SqlRiskStore(configured, otherAccount).ReadCapital(CancellationToken.None) is null,
            "Risk capital cannot cross account partitions."
        );
        await using var oldRisk = new SqlCommand(
            "UPDATE dbo.CapitalSnapshots SET Payload=JSON_MODIFY(Payload,'$.Source',NULL) WHERE AccountKey=@account;",
            connection
        );
        oldRisk.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        await oldRisk.ExecuteNonQueryAsync();
        Check(
            (await risk.ReadCapital(CancellationToken.None))?.Source == CapitalSource.Unknown,
            "Legacy risk payload remains unavailable until fresh Broker evidence."
        );
        await using var mirror = new SqlCommand(
            "SELECT Payload FROM dbo.TradingFills WHERE AccountKey=@account;",
            connection
        );
        mirror.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        var mirrored = await mirror.ExecuteScalarAsync() as string;
        using var mirrorDocument = JsonDocument.Parse(
            mirrored ?? throw new InvalidOperationException("Fill mirror missing.")
        );
        Check(
            mirrorDocument.RootElement.GetProperty("fill").GetProperty("feeThb").GetDecimal() == .25m,
            "Canonical fill mirror retains exact net-fee evidence."
        );
        Console.WriteLine(
            "Trading persistence SQL passed: synthetic legacy JSON, stop/restart, CAS, atomic immutable rollback, exact fees and account isolation. No broker or service commands."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
