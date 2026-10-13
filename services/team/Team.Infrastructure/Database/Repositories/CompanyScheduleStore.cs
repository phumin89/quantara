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
            SELECT Enabled,NextRunAt,LastRunId FROM dbo.CompanySchedule WHERE Id=1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Company schedule is missing.");
        }

        return new CompanySchedule(
            reader.GetBoolean(0),
            reader.GetDateTimeOffset(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2)
        );
    }

    public async Task Save(CompanySchedule schedule, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE dbo.CompanySchedule SET Enabled=@enabled,NextRunAt=@next,LastRunId=@last WHERE Id=1;";
        command.Parameters.Add("@enabled", SqlDbType.Bit).Value = schedule.Enabled;
        command.Parameters.Add("@next", SqlDbType.DateTimeOffset).Value = schedule.NextRunAt;
        command.Parameters.Add("@last", SqlDbType.UniqueIdentifier).Value = schedule.LastRunId.HasValue
            ? schedule.LastRunId.Value
            : DBNull.Value;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Company schedule is missing.");
        }
    }
}
