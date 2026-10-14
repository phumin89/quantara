using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class TeamRunStore : ITeamRunStore, IDisposable
{
    private readonly string connectionString;

    private readonly bool createLocalDatabase;

    private readonly SemaphoreSlim initialization = new(1, 1);

    private bool initialized;

    public TeamRunStore(string connectionString, bool createLocalDatabase = false)
    {
        this.connectionString = connectionString;
        this.createLocalDatabase = createLocalDatabase;
    }

    public async Task<TeamRun?> Latest(CancellationToken cancellationToken)
    {
        return await this.Read(null, cancellationToken);
    }

    public async Task<TeamRun?> Find(Guid id, CancellationToken cancellationToken)
    {
        return await this.Read(id, cancellationToken);
    }

    public async Task<TeamRun?> LatestForAccount(string accountKey, CancellationToken cancellationToken)
    {
        await this.Initialize(cancellationToken);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TOP (1) Payload FROM dbo.TeamRuns WHERE JSON_VALUE(Payload,'$.AccountKey')=@account ORDER BY CreatedAt DESC, Id DESC";
        command.Parameters.Add("@account", SqlDbType.VarChar, 64).Value = accountKey;
        var payload = await command.ExecuteScalarAsync(cancellationToken);
        return payload is string json ? JsonSerializer.Deserialize<TeamRun>(json) : null;
    }

    public async Task<TeamRun?> LatestReviewed(
        string accountKey,
        DateTimeOffset before,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(accountKey);
        await this.Initialize(cancellationToken);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) Payload FROM dbo.TeamRuns
            CROSS APPLY OPENJSON(Payload, '$.Steps')
            WITH (Role int '$.Role', Status int '$.Status', Output nvarchar(max) '$.Output') AS Step
            WHERE CreatedAt < @before
              AND JSON_VALUE(Payload, '$.AccountKey') = @account
              AND Step.Role = @reviewer
              AND Step.Status = @completed
              AND NULLIF(LTRIM(RTRIM(Step.Output)), '') IS NOT NULL
            ORDER BY CreatedAt DESC, Id DESC
            """;
        command.Parameters.Add("@before", SqlDbType.DateTimeOffset).Value = before;
        command.Parameters.Add("@account", SqlDbType.VarChar, 64).Value = accountKey;
        command.Parameters.Add("@reviewer", SqlDbType.Int).Value = (int)TeamRole.Reviewer;
        command.Parameters.Add("@completed", SqlDbType.Int).Value = (int)TeamRunStatus.Completed;
        var payload = await command.ExecuteScalarAsync(cancellationToken);
        return payload is string json
            ? JsonSerializer.Deserialize<TeamRun>(json)
                ?? throw new InvalidOperationException("Stored team task is invalid.")
            : null;
    }

    public async Task<IReadOnlyList<TeamRun>> LearningHistory(
        string accountKey,
        DateTimeOffset before,
        CancellationToken cancellationToken
    )
    {
        ValidateAccountKey(accountKey);
        await this.Initialize(cancellationToken);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH Ranked AS (
                SELECT Payload, CreatedAt, Id,
                    CONVERT(date, SWITCHOFFSET(CreatedAt, '+07:00')) AS BangkokDate,
                    ROW_NUMBER() OVER (
                        PARTITION BY CONVERT(date, SWITCHOFFSET(CreatedAt, '+07:00'))
                        ORDER BY CreatedAt DESC, Id DESC
                    ) AS RankInDay
                FROM dbo.TeamRuns
                WHERE CreatedAt < @before
                  AND CreatedAt >= @start
                  AND JSON_VALUE(Payload, '$.AccountKey') = @account
                  AND JSON_VALUE(Payload, '$.Status') = @completed
                  AND JSON_QUERY(Payload, '$.Learning') IS NOT NULL
            )
            SELECT Payload FROM Ranked
            WHERE RankInDay = 1
            ORDER BY CreatedAt DESC, Id DESC
            """;
        var bangkokDate = before.ToOffset(TimeSpan.FromHours(7)).Date;
        command.Parameters.Add("@before", SqlDbType.DateTimeOffset).Value = before;
        command.Parameters.Add("@start", SqlDbType.DateTimeOffset).Value = new DateTimeOffset(
            bangkokDate.AddDays(-6),
            TimeSpan.FromHours(7)
        );
        command.Parameters.Add("@account", SqlDbType.VarChar, 64).Value = accountKey;
        command.Parameters.Add("@completed", SqlDbType.Int).Value = (int)TeamRunStatus.Completed;
        var runs = new List<TeamRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(
                JsonSerializer.Deserialize<TeamRun>(reader.GetString(0))
                    ?? throw new InvalidOperationException("Stored team task is invalid.")
            );
        }

        return runs;
    }

    public async Task<IReadOnlyList<TeamRun>> ForDay(DateOnly day, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        var end = start.AddDays(1);
        await this.Initialize(cancellationToken);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Payload FROM dbo.TeamRuns WHERE CreatedAt >= @start AND CreatedAt < @end ORDER BY CreatedAt, Id";
        command.Parameters.Add("@start", SqlDbType.DateTimeOffset).Value = start;
        command.Parameters.Add("@end", SqlDbType.DateTimeOffset).Value = end;
        var runs = new List<TeamRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(
                JsonSerializer.Deserialize<TeamRun>(reader.GetString(0))
                    ?? throw new InvalidOperationException("Stored team task is invalid.")
            );
        }

        return runs;
    }

    public async Task Save(TeamRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await this.Initialize(cancellationToken);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.TeamRuns SET Payload=@payload, UpdatedAt=@updated, ActiveSlot=@active WHERE Id=@id;
            IF @@ROWCOUNT=0 INSERT dbo.TeamRuns(Id,CreatedAt,UpdatedAt,ActiveSlot,Payload)
            VALUES(@id,@created,@updated,@active,@payload);
            """;
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = run.Id;
        command.Parameters.Add("@created", SqlDbType.DateTimeOffset).Value = run.CreatedAt;
        command.Parameters.Add("@updated", SqlDbType.DateTimeOffset).Value = run.UpdatedAt;
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(run);
        command.Parameters.Add("@active", SqlDbType.Int).Value = run.Status
            is TeamRunStatus.Queued
                or TeamRunStatus.Running
            ? 1
            : DBNull.Value;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new TeamBusyException();
        }
    }

    public async Task Recover(CancellationToken cancellationToken)
    {
        await this.Initialize(cancellationToken);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM dbo.TeamRuns WHERE ActiveSlot=1";
        var runs = new List<TeamRun>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var run =
                    JsonSerializer.Deserialize<TeamRun>(reader.GetString(0))
                    ?? throw new InvalidOperationException("Stored team task is invalid.");
                runs.Add(run);
            }
        }

        foreach (var run in runs)
        {
            await this.Save(
                run with
                {
                    Status = TeamRunStatus.Failed,
                    Error = "The service restarted before this task finished. Start a new task to retry.",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Steps = run
                        .Steps.Select(step =>
                            step.Status is TeamRunStatus.Queued or TeamRunStatus.Running
                                ? step with
                                {
                                    Status = TeamRunStatus.Failed,
                                    Error = "Interrupted by service restart.",
                                }
                                : step
                        )
                        .ToArray(),
                },
                cancellationToken
            );
        }
    }

    public void Dispose()
    {
        this.initialization.Dispose();
    }

    private async Task<TeamRun?> Read(Guid? id, CancellationToken cancellationToken)
    {
        await this.Initialize(cancellationToken);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = id.HasValue
            ? "SELECT Payload FROM dbo.TeamRuns WHERE Id=@id"
            : "SELECT TOP (1) Payload FROM dbo.TeamRuns ORDER BY CreatedAt DESC, Id DESC";
        if (id.HasValue)
        {
            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id.Value;
        }

        var payload = await command.ExecuteScalarAsync(cancellationToken);

        return payload is string json
            ? JsonSerializer.Deserialize<TeamRun>(json)
                ?? throw new InvalidOperationException("Stored team task is invalid.")
            : null;
    }

    private async Task Initialize(CancellationToken cancellationToken)
    {
        await this.initialization.WaitAsync(cancellationToken);
        try
        {
            if (this.initialized)
            {
                return;
            }

            if (this.createLocalDatabase)
            {
                var master = new SqlConnectionStringBuilder(this.connectionString) { InitialCatalog = "master" };
                await using var connection = new SqlConnection(master.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var create = connection.CreateCommand();
                create.CommandText = "IF DB_ID(N'QuantaraTeam') IS NULL CREATE DATABASE [QuantaraTeam]";
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var database = new SqlConnection(this.connectionString);
            await database.OpenAsync(cancellationToken);
            await using var migration = database.CreateCommand();
            migration.CommandText = """
                IF OBJECT_ID(N'dbo.TeamRuns',N'U') IS NULL
                BEGIN
                  CREATE TABLE dbo.TeamRuns (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    CreatedAt datetimeoffset NOT NULL,
                    UpdatedAt datetimeoffset NOT NULL,
                    ActiveSlot int NULL,
                    Payload nvarchar(max) NOT NULL
                  );
                  CREATE UNIQUE INDEX UX_TeamRuns_ActiveSlot ON dbo.TeamRuns(ActiveSlot) WHERE ActiveSlot IS NOT NULL;
                END;
                """;
            await migration.ExecuteNonQueryAsync(cancellationToken);
            this.initialized = true;
        }
        finally
        {
            this.initialization.Release();
        }
    }

    private static void ValidateAccountKey(string accountKey)
    {
        if (accountKey is null || accountKey.Length != 64 || accountKey.Any(character => !IsHexDigit(character)))
        {
            throw new ArgumentException("Account key must be exactly 64 hexadecimal characters.", nameof(accountKey));
        }
    }

    private static bool IsHexDigit(char character)
    {
        return character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}
