using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ContinuousResearchSqlChecks
{
    public static async Task Run()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUANTARA_FEATURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (
            !new SqlConnectionStringBuilder(connectionString).InitialCatalog.StartsWith(
                "QuantaraFeatureChecks",
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException("Continuous research checks require an isolated feature database.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.CompanySchedule',N'U') IS NOT NULL
                THROW 51000, 'Migration proof requires a fresh isolated feature database.', 1;
            CREATE TABLE dbo.CompanySchedule (
                Id int NOT NULL PRIMARY KEY CHECK (Id=1), Enabled bit NOT NULL,
                NextRunAt datetimeoffset NOT NULL, LastRunId uniqueidentifier NULL,
                LastSignalKey varchar(64) NULL, LastAnalysisAt datetimeoffset NULL);
            INSERT dbo.CompanySchedule(Id,Enabled,NextRunAt,LastSignalKey)
                VALUES(1,0,'2026-10-20T02:00:00+07:00','legacy-signal');
            """;
        await command.ExecuteNonQueryAsync();
        var schedules = new CompanyScheduleStore(connectionString);
        var legacy = await schedules.Read(CancellationToken.None);
        Check(
            !legacy.Enabled
                && legacy.LastSignalKey == "legacy-signal"
                && legacy.DeferredReason is null
                && legacy.NextRole is null,
            "Additive schedule migration preserves the owner's stop and legacy evidence."
        );
        var saved = legacy with
        {
            DeferredReason = "ordinary_research_limit",
            NextRole = TeamRole.News,
            LastRunId = Guid.NewGuid(),
        };
        await schedules.Save(saved, CancellationToken.None);
        Check(
            await new CompanyScheduleStore(connectionString).Read(CancellationToken.None) == saved,
            "Deferred reason, exact eligibility and role survive a SQL store restart."
        );

        var now = DateTimeOffset.UtcNow;
        var account = new string('d', 64);
        var old = new TeamRun(
            Guid.NewGuid(),
            "legacy fixture",
            TeamRunStatus.Completed,
            now.AddHours(-2),
            now.AddHours(-1),
            [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, "legacy result", null)],
            null,
            account
        );
        using var store = new TeamRunStore(connectionString);
        await store.Save(old, CancellationToken.None);
        var payload =
            JsonSerializer.SerializeToNode(old) ?? throw new InvalidOperationException("Missing fixture payload.");
        payload.AsObject().Remove("Task");
        var oldStep = payload["Steps"]?[0]?.AsObject() ?? throw new InvalidOperationException("Missing fixture step.");
        foreach (var key in new[] { "StartedAt", "CompletedAt", "InputDigest", "Usage" })
        {
            oldStep.Remove(key);
        }

        command.CommandText = "UPDATE dbo.TeamRuns SET Payload=@payload WHERE Id=@id;";
        command.Parameters.AddWithValue("@payload", payload.ToJsonString());
        command.Parameters.AddWithValue("@id", old.Id);
        await command.ExecuteNonQueryAsync();
        var retained = await store.Find(old.Id, CancellationToken.None);
        Check(
            retained?.Task is null
                && retained?.Steps.Single().Usage is null
                && retained?.Steps.Single().Output == "legacy result",
            "Pre-telemetry report JSON still loads without inventing usage."
        );
        var digest = new string('e', 64);
        var current = old with
        {
            Id = Guid.NewGuid(),
            Status = TeamRunStatus.Running,
            CreatedAt = now,
            UpdatedAt = now,
            Task = new ResearchTask(
                TeamRole.News,
                digest,
                null,
                now,
                now.AddMinutes(20),
                digest,
                NewsRevisions: ["event@1"]
            ),
            Steps =
            [
                new TeamStep(
                    TeamRole.News,
                    TeamRunStatus.Running,
                    null,
                    null,
                    now,
                    InputDigest: digest,
                    Usage: new ResearchUsage(30, 6, 2)
                ),
            ],
        };
        await store.Save(current, CancellationToken.None);
        using var restarted = new TeamRunStore(connectionString);
        await restarted.Recover(CancellationToken.None);
        var recovered = await restarted.Find(current.Id, CancellationToken.None);
        Check(
            recovered?.Status == TeamRunStatus.Failed
                && JsonSerializer.Serialize(recovered.Task) == JsonSerializer.Serialize(current.Task)
                && recovered.Steps.Single().Usage == current.Steps.Single().Usage,
            "Recovery retains task lineage and charged usage without replaying an interrupted model task."
        );
        Check(
            (await restarted.Find(old.Id, CancellationToken.None))?.Steps.Single().Output == "legacy result",
            "Recovery preserves the original completed report."
        );
        Console.WriteLine(
            "Continuous research SQL: additive migration, durable status, legacy reports and interrupted-task recovery passed."
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
