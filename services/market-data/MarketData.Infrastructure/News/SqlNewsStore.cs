using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.MarketData.Application.News;
using Quantara.MarketData.Domain.News;

namespace Quantara.MarketData.Infrastructure.News;

public sealed class SqlNewsStore(string connectionString, bool createLocalDatabase) : INewsReader
{
    private readonly string connectionString = connectionString;

    private readonly bool createLocalDatabase = createLocalDatabase;

    public async Task Initialize(CancellationToken cancellationToken)
    {
        if (this.createLocalDatabase)
        {
            var builder = new SqlConnectionStringBuilder(this.connectionString) { InitialCatalog = "master" };
            await using var master = new SqlConnection(builder.ConnectionString);
            await master.OpenAsync(cancellationToken);
            await using var create = master.CreateCommand();
            create.CommandText = "IF DB_ID(N'QuantaraMarketData') IS NULL CREATE DATABASE QuantaraMarketData;";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.NewsItems', N'U') IS NULL
            CREATE TABLE dbo.NewsItems (EventId varchar(64) NOT NULL, Revision int NOT NULL, SourceId varchar(32) NOT NULL, Payload nvarchar(max) NOT NULL, RevisionSeenAt datetimeoffset NOT NULL, CONSTRAINT PK_NewsItems PRIMARY KEY(EventId, Revision));
            IF OBJECT_ID(N'dbo.NewsSources', N'U') IS NULL
            CREATE TABLE dbo.NewsSources (SourceId varchar(32) NOT NULL PRIMARY KEY, Label nvarchar(200) NOT NULL, Url nvarchar(1000) NOT NULL, Status varchar(32) NOT NULL, LastSuccessAt datetimeoffset NULL, NextRetryAt datetimeoffset NULL, Reason nvarchar(500) NULL, ETag nvarchar(500) NULL, LastModified nvarchar(500) NULL);
            IF OBJECT_ID(N'dbo.NewsPilot', N'U') IS NULL
            CREATE TABLE dbo.NewsPilot(Id int NOT NULL PRIMARY KEY CHECK(Id=1), StartedAt datetimeoffset NOT NULL, LastPollAt datetimeoffset NOT NULL, GapCount int NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Save(
        NewsSource source,
        IReadOnlyList<NewsEvent> events,
        NewsSourceState state,
        string? etag,
        string? lastModified,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        foreach (var item in events)
        {
            var revision = item.Revision;
            DateTimeOffset firstSeenAt = item.FirstSeenAt;
            await using (var existing = connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText =
                    "SELECT TOP (1) Payload, Revision FROM dbo.NewsItems WITH (UPDLOCK, HOLDLOCK) WHERE EventId=@id ORDER BY Revision DESC";
                existing.Parameters.Add("@id", SqlDbType.VarChar, 64).Value = item.EventId;
                await using var existingReader = await existing.ExecuteReaderAsync(cancellationToken);
                if (await existingReader.ReadAsync(cancellationToken))
                {
                    var previous = JsonSerializer.Deserialize<NewsEvent>(existingReader.GetString(0));
                    if (previous is not null && previous.ContentHash == item.ContentHash)
                    {
                        continue;
                    }

                    revision = existingReader.GetInt32(1) + 1;
                    firstSeenAt = previous?.FirstSeenAt ?? item.FirstSeenAt;
                }
            }

            var storedItem = item with
            {
                Revision = revision,
                FirstSeenAt = firstSeenAt,
                RevisionSeenAt = item.RevisionSeenAt,
            };
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "IF NOT EXISTS (SELECT 1 FROM dbo.NewsItems WHERE EventId=@id AND Revision=@revision) INSERT dbo.NewsItems(EventId,Revision,SourceId,Payload,RevisionSeenAt) VALUES(@id,@revision,@source,@payload,@seen);";
            command.Parameters.Add("@id", SqlDbType.VarChar, 64).Value = item.EventId;
            command.Parameters.Add("@revision", SqlDbType.Int).Value = storedItem.Revision;
            command.Parameters.Add("@source", SqlDbType.VarChar, 32).Value = item.SourceId;
            command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(storedItem);
            command.Parameters.Add("@seen", SqlDbType.DateTimeOffset).Value = storedItem.RevisionSeenAt;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var stateCommand = connection.CreateCommand();
        stateCommand.Transaction = transaction;
        stateCommand.CommandText =
            "MERGE dbo.NewsSources WITH(HOLDLOCK) AS target USING (SELECT @id AS SourceId) AS source ON target.SourceId=source.SourceId WHEN MATCHED THEN UPDATE SET Status=@status,LastSuccessAt=COALESCE(@success,target.LastSuccessAt),NextRetryAt=@retry,Reason=@reason,ETag=COALESCE(@etag,target.ETag),LastModified=COALESCE(@modified,target.LastModified) WHEN NOT MATCHED THEN INSERT(SourceId,Label,Url,Status,LastSuccessAt,NextRetryAt,Reason,ETag,LastModified) VALUES(@id,@label,@url,@status,@success,@retry,@reason,@etag,@modified);";
        stateCommand.Parameters.Add("@id", SqlDbType.VarChar, 32).Value = source.Id;
        stateCommand.Parameters.Add("@label", SqlDbType.NVarChar, 200).Value = source.Label;
        stateCommand.Parameters.Add("@url", SqlDbType.NVarChar, 1000).Value = source.Url.ToString();
        stateCommand.Parameters.Add("@status", SqlDbType.VarChar, 32).Value = state.Status;
        stateCommand.Parameters.Add("@success", SqlDbType.DateTimeOffset).Value =
            (object?)state.LastSuccessAt ?? DBNull.Value;
        stateCommand.Parameters.Add("@retry", SqlDbType.DateTimeOffset).Value =
            (object?)state.NextRetryAt ?? DBNull.Value;
        stateCommand.Parameters.Add("@reason", SqlDbType.NVarChar, 500).Value = (object?)state.Reason ?? DBNull.Value;
        stateCommand.Parameters.Add("@etag", SqlDbType.NVarChar, 500).Value = (object?)etag ?? DBNull.Value;
        stateCommand.Parameters.Add("@modified", SqlDbType.NVarChar, 500).Value = (object?)lastModified ?? DBNull.Value;
        await stateCommand.ExecuteNonQueryAsync(cancellationToken);
        await using var pilot = connection.CreateCommand();
        pilot.Transaction = transaction;
        pilot.CommandText = """
            DECLARE @bad bit=CASE WHEN EXISTS(SELECT 1 FROM dbo.NewsSources WHERE Status NOT IN('Healthy','NoNewEvents','UnsupportedSource')) OR (SELECT COUNT(*) FROM dbo.NewsSources WHERE SourceId IN('federal-reserve','bls'))<2 THEN 1 ELSE 0 END;
            IF NOT EXISTS(SELECT 1 FROM dbo.NewsPilot WITH(UPDLOCK,HOLDLOCK) WHERE Id=1)
                INSERT dbo.NewsPilot(Id,StartedAt,LastPollAt,GapCount) VALUES(1,@now,@now,CASE WHEN @bad=1 THEN 1 ELSE 0 END);
            ELSE UPDATE dbo.NewsPilot SET
                StartedAt=CASE WHEN @bad=1 OR DATEDIFF(second,LastPollAt,@now)>600 THEN @now ELSE StartedAt END,
                GapCount=GapCount+CASE WHEN @bad=1 OR DATEDIFF(second,LastPollAt,@now)>600 THEN 1 ELSE 0 END,
                LastPollAt=@now WHERE Id=1;
            """;
        pilot.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = DateTimeOffset.UtcNow;
        await pilot.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<NewsSourceValidator?> ReadValidator(string sourceId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ETag, LastModified, NextRetryAt FROM dbo.NewsSources WHERE SourceId=@id";
        command.Parameters.Add("@id", SqlDbType.VarChar, 32).Value = sourceId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new NewsSourceValidator(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2)
        );
    }

    public async Task<NewsPacket> Read(string? symbols, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var wanted = (symbols ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .ToHashSet();
        var events = new List<NewsEvent>();
        var states = new List<NewsSourceState>();
        var cutoff = asOf ?? DateTimeOffset.UtcNow;
        if (
            cutoff > DateTimeOffset.UtcNow.AddSeconds(5)
            || cutoff < DateTimeOffset.UnixEpoch
            || wanted.Count > 16
            || wanted.Any(symbol => symbol.Length is < 1 or > 30 || !symbol.All(char.IsAsciiLetterOrDigit))
        )
        {
            throw new ArgumentException("Invalid news scope or cutoff.");
        }

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var sources = connection.CreateCommand())
        {
            sources.CommandText =
                "SELECT SourceId,Label,Url,Status,LastSuccessAt,NextRetryAt,Reason FROM dbo.NewsSources ORDER BY SourceId;";
            await using var rows = await sources.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken))
            {
                var success = rows.IsDBNull(4) ? null : (DateTimeOffset?)rows.GetFieldValue<DateTimeOffset>(4);
                var status = rows.GetString(3);
                if (
                    success is { } at
                    && DateTimeOffset.UtcNow - at > TimeSpan.FromMinutes(15)
                    && status is "Healthy" or "NoNewEvents"
                )
                {
                    status = "Stale";
                }

                states.Add(
                    new(
                        rows.GetString(0),
                        rows.GetString(1),
                        rows.GetString(2),
                        status,
                        success,
                        rows.IsDBNull(5) ? null : rows.GetFieldValue<DateTimeOffset>(5),
                        rows.IsDBNull(6) ? null : rows.GetString(6)
                    )
                );
            }
        }

        foreach (
            var source in new[]
            {
                new NewsSource(
                    "federal-reserve",
                    "Federal Reserve",
                    new Uri("https://www.federalreserve.gov/feeds/press_all.xml")
                ),
                new NewsSource("bls", "BLS", new Uri("https://www.bls.gov/feed/bls_latest.rss")),
            }
        )
        {
            if (!states.Any(state => state.Id == source.Id))
            {
                states.Add(
                    new(
                        source.Id,
                        source.Label,
                        source.Url.ToString(),
                        "Unavailable",
                        null,
                        null,
                        "No successful poll recorded."
                    )
                );
            }
        }

        states.Add(
            new(
                "bitkub",
                "Bitkub announcements",
                "https://support.bitkub.com/",
                "UnsupportedSource",
                null,
                null,
                "No production news feed has been verified; broker status is not inferred from macro news."
            )
        );
        NewsPilotStatus pilotStatus;
        await using (var pilotQuery = connection.CreateCommand())
        {
            pilotQuery.CommandText = "SELECT StartedAt,LastPollAt,GapCount FROM dbo.NewsPilot WHERE Id=1;";
            await using var row = await pilotQuery.ExecuteReaderAsync(cancellationToken);
            if (await row.ReadAsync(cancellationToken))
            {
                var startedAt = row.GetFieldValue<DateTimeOffset>(0);
                var lastPollAt = row.GetFieldValue<DateTimeOffset>(1);
                pilotStatus = new(
                    startedAt,
                    lastPollAt,
                    DateTimeOffset.UtcNow - startedAt >= TimeSpan.FromHours(72)
                        && DateTimeOffset.UtcNow - lastPollAt <= TimeSpan.FromMinutes(10)
                        && states
                            .Where(state => state.Id != "bitkub")
                            .All(state => state.Status is "Healthy" or "NoNewEvents"),
                    row.GetInt32(2)
                );
            }
            else
            {
                pilotStatus = new(null, null, false, 0);
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Payload FROM (SELECT Payload, RevisionSeenAt, ROW_NUMBER() OVER (PARTITION BY EventId ORDER BY Revision DESC) AS rn FROM dbo.NewsItems WHERE RevisionSeenAt <= @asOf) AS latest WHERE rn = 1 AND (TRY_CONVERT(datetimeoffset,JSON_VALUE(Payload,'$.PublishedAt')) IS NULL OR TRY_CONVERT(datetimeoffset,JSON_VALUE(Payload,'$.PublishedAt'))<=@asOf) AND TRY_CONVERT(datetimeoffset,JSON_VALUE(Payload,'$.ExpiresAt'))>@asOf ORDER BY RevisionSeenAt DESC";
        command.Parameters.Add("@asOf", SqlDbType.DateTimeOffset).Value = cutoff;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = JsonSerializer.Deserialize<NewsEvent>(reader.GetString(0));
            if (item is not null && (wanted.Count == 0 || item.MarketWide || item.Symbols.Any(wanted.Contains)))
            {
                events.Add(item);
            }
        }

        var packet = new NewsPacket(
            "news-v1",
            DateTimeOffset.UtcNow,
            false,
            events.Take(5).ToArray(),
            states,
            Math.Max(0, events.Count - 5),
            pilotStatus
        );
        return NewsPacketBudget.Fit(packet);
    }
}
