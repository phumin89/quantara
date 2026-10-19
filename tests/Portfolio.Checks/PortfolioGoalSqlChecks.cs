using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class PortfolioGoalSqlChecks
{
    public static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("QUANTARA_FEATURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("Portfolio goal SQL checks skipped (opt-in connection is not configured).");
            return;
        }

        if (
            !new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith(
                "QuantaraFeatureChecks",
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException("SQL goal checks require an isolated QuantaraFeatureChecks database.");
        }

        var account = Guid.NewGuid().ToString("N") + new string('a', 32);
        var store = new SqlPortfolioGoalStore(connection, account);
        await store.Initialize(CancellationToken.None);
        var observed = DateTimeOffset.UtcNow;
        var checkpoint = new PortfolioGoalCheckpoint(
            account,
            PortfolioObjective.Id,
            observed,
            9000,
            7000,
            null,
            "fixture"
        );
        await store.Save(checkpoint, CancellationToken.None);
        await store.Save(checkpoint with { PortfolioValueThb = 1 }, CancellationToken.None);
        var reread = await new SqlPortfolioGoalStore(connection, account).Read(observed, CancellationToken.None);
        Check(
            reread?.PortfolioValueThb == 9000,
            "SQL goal checkpoint round-trips and duplicate timestamp is immutable."
        );
        Check(
            await new SqlPortfolioGoalStore(connection, new string('b', 64)).Read(observed, CancellationToken.None)
                is null,
            "SQL goal checkpoints remain account isolated."
        );
        Check(
            await store.Read(observed.AddSeconds(-1), CancellationToken.None) is null,
            "SQL goal cutoff excludes late checkpoints."
        );
        var missingDatabase = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = new SqlConnectionStringBuilder(connection).InitialCatalog + "Missing",
            ConnectTimeout = 2,
        };
        var state = TradingStateFactory.Create(observed);
        var projection = new TradingOverviewProjection(
            new AccountContext(account),
            new TradingControlOptions(""),
            new FixedTimeProvider(observed)
        );
        var handler = new GetTradingOverviewHandler(
            new FakeTradingStore(state),
            projection,
            new SqlPortfolioGoalStore(missingDatabase.ConnectionString, account)
        );
        using var status = JsonDocument.Parse(
            await handler.Handle(new GetTradingOverviewQuery(), CancellationToken.None)
        );
        Check(
            status.RootElement.GetProperty("objective").GetProperty("valuationStatus").GetString() == "unknown"
                && status.RootElement.GetProperty("accountKey").GetString() == account,
            "Optional goal SQL failure preserves the trading overview."
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
