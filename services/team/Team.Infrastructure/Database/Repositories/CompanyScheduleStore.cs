using System.Data;
using Microsoft.Data.SqlClient;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class CompanyScheduleStore : ICompanyScheduleStore
{
    private readonly string connectionString;

    public CompanyScheduleStore(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public async Task<CompanySchedule> Read(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.CompanySchedule',N'U') IS NULL
            BEGIN
              CREATE TABLE dbo.CompanySchedule (
                Id int NOT NULL PRIMARY KEY CHECK (Id=1),
                Enabled bit NOT NULL,
                NextRunAt datetimeoffset NOT NULL,
                LastRunId uniqueidentifier NULL
              );
              INSERT dbo.CompanySchedule(Id,Enabled,NextRunAt) VALUES(1,1,SYSDATETIMEOFFSET());
            END;
            IF COL_LENGTH('dbo.CompanySchedule','LastSignalKey') IS NULL
                ALTER TABLE dbo.CompanySchedule ADD LastSignalKey varchar(64) NULL;
            IF COL_LENGTH('dbo.CompanySchedule','LastAnalysisAt') IS NULL
                ALTER TABLE dbo.CompanySchedule ADD LastAnalysisAt datetimeoffset NULL;
            IF COL_LENGTH('dbo.CompanySchedule','DeferredReason') IS NULL
                ALTER TABLE dbo.CompanySchedule ADD DeferredReason varchar(80) NULL;
            IF COL_LENGTH('dbo.CompanySchedule','NextRole') IS NULL
                ALTER TABLE dbo.CompanySchedule ADD NextRole int NULL;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText =
            "SELECT Enabled,NextRunAt,LastRunId,LastSignalKey,LastAnalysisAt,DeferredReason,NextRole FROM dbo.CompanySchedule WHERE Id=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Company schedule is missing.");
        }

        return new CompanySchedule(
            reader.GetBoolean(0),
            reader.GetDateTimeOffset(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : (TeamRole)reader.GetInt32(6)
        );
    }

    public async Task Save(CompanySchedule schedule, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE dbo.CompanySchedule SET Enabled=@enabled,NextRunAt=@next,LastRunId=@last,LastSignalKey=@signal,LastAnalysisAt=@analysis,DeferredReason=@reason,NextRole=@role WHERE Id=1;";
        command.Parameters.Add("@enabled", SqlDbType.Bit).Value = schedule.Enabled;
        command.Parameters.Add("@next", SqlDbType.DateTimeOffset).Value = schedule.NextRunAt;
        command.Parameters.Add("@last", SqlDbType.UniqueIdentifier).Value = schedule.LastRunId.HasValue
            ? schedule.LastRunId.Value
            : DBNull.Value;
        command.Parameters.Add("@signal", SqlDbType.VarChar, 64).Value =
            (object?)schedule.LastSignalKey ?? DBNull.Value;
        command.Parameters.Add("@analysis", SqlDbType.DateTimeOffset).Value =
            (object?)schedule.LastAnalysisAt ?? DBNull.Value;
        command.Parameters.Add("@reason", SqlDbType.VarChar, 80).Value =
            (object?)schedule.DeferredReason ?? DBNull.Value;
        command.Parameters.Add("@role", SqlDbType.Int).Value = schedule.NextRole is { } role ? (int)role : DBNull.Value;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Company schedule is missing.");
        }
    }
}
