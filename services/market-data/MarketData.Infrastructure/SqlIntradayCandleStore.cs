using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public sealed class SqlIntradayCandleStore : IIntradayCandleStore
{
    private readonly string connectionString;

    private readonly bool createLocalDatabase;

    public SqlIntradayCandleStore(string connectionString, bool createLocalDatabase)
    {
        this.connectionString = connectionString;
        this.createLocalDatabase = createLocalDatabase;
    }

    public async Task Initialize(CancellationToken cancellationToken)
    {
        if (this.createLocalDatabase)
        {
            var masterBuilder = new SqlConnectionStringBuilder(this.connectionString) { InitialCatalog = "master" };
            await using var master = new SqlConnection(masterBuilder.ConnectionString);
            await master.OpenAsync(cancellationToken);
            await using var databaseCommand = master.CreateCommand();
            databaseCommand.CommandText = "IF DB_ID(N'QuantaraMarketData') IS NULL CREATE DATABASE QuantaraMarketData;";
            await databaseCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.IntradayCandles', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.IntradayCandles
                (
                    Symbol varchar(34) NOT NULL,
                    OpenTime bigint NOT NULL,
                    Payload nvarchar(max) NOT NULL,
                    CONSTRAINT PK_IntradayCandles PRIMARY KEY (Symbol, OpenTime)
                );
            END
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Save(string symbol, IReadOnlyList<MarketCandle> candles, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol) || candles.Count == 0)
        {
            return;
        }

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        foreach (var candle in candles)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                IF EXISTS (SELECT 1 FROM dbo.IntradayCandles WITH (UPDLOCK, SERIALIZABLE) WHERE Symbol = @symbol AND OpenTime = @openTime AND Payload <> @payload)
                    THROW 51001, 'An immutable intraday candle conflicts with stored data.', 1;
                IF NOT EXISTS (SELECT 1 FROM dbo.IntradayCandles WHERE Symbol = @symbol AND OpenTime = @openTime)
                    INSERT dbo.IntradayCandles (Symbol, OpenTime, Payload) VALUES (@symbol, @openTime, @payload);
                """;
            command.Parameters.Add("@symbol", System.Data.SqlDbType.VarChar, 34).Value = symbol.ToUpperInvariant();
            command.Parameters.Add("@openTime", System.Data.SqlDbType.BigInt).Value = candle.Timestamp;
            command.Parameters.Add("@payload", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
                candle with
                {
                    Open = SqlIntradayCandleStore.Normalize(candle.Open),
                    High = SqlIntradayCandleStore.Normalize(candle.High),
                    Low = SqlIntradayCandleStore.Normalize(candle.Low),
                    Close = SqlIntradayCandleStore.Normalize(candle.Close),
                    Volume = SqlIntradayCandleStore.Normalize(candle.Volume),
                }
            );
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketCandle>> Read(string symbol, CancellationToken cancellationToken)
    {
        var result = new List<MarketCandle>();
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TOP (120) Payload FROM dbo.IntradayCandles WHERE Symbol = @symbol ORDER BY OpenTime DESC";
        command.Parameters.Add("@symbol", System.Data.SqlDbType.VarChar, 34).Value = symbol.ToUpperInvariant();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                JsonSerializer.Deserialize<MarketCandle>(reader.GetString(0))
                    ?? throw new InvalidDataException("Stored candle is invalid.")
            );
        }

        result.Reverse();
        return result;
    }

    private static decimal Normalize(decimal value) =>
        decimal.Parse(
            value.ToString("G29", CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture
        );
}
