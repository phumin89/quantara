using Microsoft.Data.SqlClient;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchBudgetSqlChecks
{
    public static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("QUANTARA_FEATURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("Research budget SQL checks skipped (opt-in connection is not configured).");
            return;
        }

        if (
            !new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith(
                "QuantaraFeatureChecks",
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "SQL budget checks require an isolated QuantaraFeatureChecks database."
            );
        }

        var suffix = Guid.NewGuid().ToString("N");
        var accountA = suffix.PadLeft(64, 'a')[..64];
        var accountB = suffix.PadLeft(64, 'b')[..64];
        var now = DateTimeOffset.UtcNow;
        var first = new SqlResearchBudget(connection);
        Check(
            (await first.TryClaim(accountA, false, now, CancellationToken.None)).Allowed,
            "First SQL budget claim is allowed."
        );
        Check(
            (
                await new SqlResearchBudget(connection).TryClaim(
                    accountA,
                    true,
                    now.AddMinutes(1),
                    CancellationToken.None
                )
            ).Allowed,
            "Reserved position-review SQL lane is allowed."
        );
        Check(
            (
                await new SqlResearchBudget(connection).TryClaim(
                    accountA,
                    false,
                    now.AddMinutes(2),
                    CancellationToken.None
                )
            ).Allowed,
            "Second ordinary SQL lane claim is allowed."
        );
        Check(
            !(
                await new SqlResearchBudget(connection).TryClaim(
                    accountA,
                    false,
                    now.AddMinutes(3),
                    CancellationToken.None
                )
            ).Allowed,
            "Fourth SQL budget claim is deferred after restart."
        );
        Check(
            (await new SqlResearchBudget(connection).TryClaim(accountB, false, now, CancellationToken.None)).Allowed,
            "Separate SQL budget account is independent."
        );
        var parallelAccount = (suffix + "c").PadLeft(64, 'c')[..64];
        Check(
            (
                await new SqlResearchBudget(connection).TryClaim(parallelAccount, false, now, CancellationToken.None)
            ).Allowed,
            "Parallel SQL case warms the isolated schema and ordinary lane."
        );
        var parallel = await Task.WhenAll(
            new SqlResearchBudget(connection).TryClaim(
                parallelAccount,
                false,
                now.AddMinutes(1),
                CancellationToken.None
            ),
            new SqlResearchBudget(connection).TryClaim(parallelAccount, true, now.AddMinutes(1), CancellationToken.None)
        );
        Check(parallel.Count(result => result.Allowed) == 2, "Parallel ordinary and priority lanes both reserve once.");
        Check(
            !(
                await new SqlResearchBudget(connection).TryClaim(
                    parallelAccount,
                    false,
                    now.AddMinutes(2),
                    CancellationToken.None
                )
            ).Allowed,
            "Extra ordinary claim is deferred at the three-claim total."
        );
        Check(
            (
                await new SqlResearchBudget(connection).TryClaim(
                    parallelAccount,
                    false,
                    now.AddHours(1).AddMinutes(1),
                    CancellationToken.None
                )
            ).Allowed,
            "Restarted SQL claim succeeds after the rolling boundary."
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
