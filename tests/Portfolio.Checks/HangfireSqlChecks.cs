using System.Data;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;
using Quantara.Portfolio.Infrastructure.Trading.Jobs;

namespace Portfolio.Checks;

public static class HangfireSqlChecks
{
    public static async Task Run()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUANTARA_FEATURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("Hangfire SQL checks skipped (opt-in connection is not configured).");
            return;
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!builder.InitialCatalog.StartsWith("QuantaraFeatureChecks", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Hangfire SQL checks require an isolated QuantaraFeatureChecks database."
            );
        }

        var account = Convert
            .ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()))
            .ToLowerInvariant();
        var destination = "destination-" + Guid.NewGuid().ToString("N");
        var store = new SqlTradingJobDispatchStore(connectionString, account);
        await SeedLegacyDispatch(connectionString);
        await store.Initialize(CancellationToken.None);

        var alert = "alert-" + Guid.NewGuid().ToString("N");
        var claims = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ => store.Claim(alert, destination, DateTimeOffset.UtcNow, CancellationToken.None))
        );
        Check(claims.Count(item => item is not null) == 1, "Concurrent claims produce one lease.");
        var lease =
            claims.Single(item => item is not null)
            ?? throw new InvalidOperationException("A dispatch lease is required.");
        Check(
            !await store.MarkEnqueued(alert, Guid.NewGuid(), "stale", CancellationToken.None),
            "A stale lease cannot acknowledge enqueue."
        );
        Check(
            await store.MarkEnqueued(alert, lease, "job-1", CancellationToken.None),
            "The current lease acknowledges enqueue."
        );
        var row =
            await store.Read(alert, CancellationToken.None)
            ?? throw new InvalidOperationException("Dispatch row was not persisted.");
        Check(
            row.JobId == "job-1" && row.DestinationKey == destination && row.Attempts == 1,
            "Dispatch row round-trips its binding and attempt."
        );
        Check(
            (
                await store.Claim(
                    alert,
                    "different-destination",
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    CancellationToken.None
                )
            )
                is null,
            "Changed destination cannot claim an existing alert."
        );

        var longAlert = new string('x', 200);
        var longLease = await store.Claim(longAlert, destination, DateTimeOffset.UtcNow, CancellationToken.None);
        Check(
            longLease is not null
                && (await store.Read(longAlert, CancellationToken.None))?.DestinationKey == destination,
            "A 200-character alert key round-trips."
        );

        await Expire(connectionString, account, longAlert);
        Check(
            !await store.MarkEnqueued(
                longAlert,
                longLease.GetValueOrDefault(),
                "expired-stale",
                CancellationToken.None
            ),
            "An expired lease cannot acknowledge enqueue."
        );
        var recovered = await store.Claim(longAlert, destination, DateTimeOffset.UtcNow, CancellationToken.None);
        Check(
            recovered is not null
                && await store.MarkEnqueued(longAlert, recovered.Value, "recovered", CancellationToken.None),
            "An expired pre-send lease can recover safely."
        );

        var capAlert = "cap-" + Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Expire(connectionString, account, capAlert);
            var token = await store.Claim(capAlert, destination, DateTimeOffset.UtcNow, CancellationToken.None);
            Check(token is not null, "Dispatch claim remains available through attempt eight.");
        }

        await Expire(connectionString, account, capAlert);
        Check(
            await store.Claim(capAlert, destination, DateTimeOffset.UtcNow, CancellationToken.None) is null,
            "Dispatch attempts are capped at eight."
        );
        await SeedCollationAlert(connectionString, account, capAlert);
        Check(
            await store.CountBlocked(destination, CancellationToken.None) == 1,
            "Dispatch blocked-count handles canonical BIN2 alert identifiers without collation conflicts."
        );
        await ExecuteQueue(connectionString, account, destination);
        Console.WriteLine("Hangfire SQL checks passed.");
    }

    private static async Task SeedLegacyDispatch(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "IF OBJECT_ID(N'dbo.TradingJobDispatch',N'U') IS NULL CREATE TABLE dbo.TradingJobDispatch(AccountKey char(64) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,AlertKey nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,DestinationKey nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,JobId nvarchar(128) NULL,LeaseToken uniqueidentifier NULL,LeaseUntil datetimeoffset NULL,DueAt datetimeoffset NOT NULL,Attempts int NOT NULL,State varchar(20) NOT NULL,PRIMARY KEY(AccountKey,AlertKey));",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteQueue(string connectionString, string account, string destination)
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Logging.ClearProviders();
        var alert = new TradingAlert(
            "sql-queue-" + Guid.NewGuid().ToString("N"),
            "fill",
            "fixture fill",
            DateTimeOffset.UtcNow
        );
        var store = new FakeTradingDeliveryStore(
            TradingStateFactory.Create(DateTimeOffset.UtcNow) with
            {
                Alerts = [alert],
            }
        );
        var notifier = new HangfireDeliveryNotifier();
        builder.Services.AddSingleton<ITradingStore>(store);
        builder.Services.AddSingleton<ITradingNotifier>(notifier);
        builder.Services.AddSingleton<IAccountContext>(new AccountContext(account));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new TradingJobOptions(account, destination, true));
        builder.Services.AddSingleton<TradingReportDelivery>();
        builder.Services.AddTransient<TradingNotificationJob>();
        builder.Services.AddHangfire(configuration =>
            configuration.UseSqlServerStorage(
                connectionString,
                new SqlServerStorageOptions
                {
                    SqlClientFactory = SqlClientFactory.Instance,
                    QueuePollInterval = TimeSpan.FromSeconds(1),
                    TryAutoDetectSchemaDependentOptions = false,
                }
            )
        );
        builder.Services.AddHangfireServer(options =>
        {
            options.WorkerCount = 1;
            options.Queues = ["telegram"];
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var jobs = host.Services.GetRequiredService<IBackgroundJobClient>();
            jobs.Enqueue<TradingNotificationJob>(job =>
                job.Deliver(account, destination, alert.Key, CancellationToken.None)
            );
            for (var attempt = 0; attempt < 100 && store.State.Alerts.Single().Status != "sent"; attempt++)
            {
                await Task.Delay(100);
            }

            Check(
                notifier.Sends == 1
                    && store.State.Alerts.Single() is { Status: "sent", MessageId: > 0, SentAt: not null },
                "SQL-backed Hangfire resolves the actual job and persists its fake Telegram receipt."
            );
            var replay = jobs.Enqueue<TradingNotificationJob>(job =>
                job.Deliver(account, destination, alert.Key, CancellationToken.None)
            );
            for (var attempt = 0; attempt < 100; attempt++)
            {
                using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
                if (connection.GetJobData(replay)?.State == "Succeeded")
                {
                    break;
                }

                await Task.Delay(100);
            }

            Check(notifier.Sends == 1, "A repeated SQL job cannot repeat a claimed Telegram side effect.");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task Expire(string connectionString, string account, string alert)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "UPDATE dbo.TradingJobDispatch SET LeaseUntil=DATEADD(second,-1,SYSDATETIMEOFFSET()),DueAt=DATEADD(second,-1,SYSDATETIMEOFFSET()),State='Claimed' WHERE AccountKey=@account AND AlertKey=@alert;",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = alert;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedCollationAlert(string connectionString, string account, string alert)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "IF OBJECT_ID(N'dbo.TradingAlerts',N'U') IS NULL CREATE TABLE dbo.TradingAlerts(AccountKey char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,Id nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,Payload nvarchar(max) NOT NULL,PRIMARY KEY(AccountKey,Id)); DELETE FROM dbo.TradingAlerts WHERE AccountKey=@account AND Id=@alert; INSERT dbo.TradingAlerts(AccountKey,Id,Payload) VALUES(@account,@alert,N'{\"status\":\"pending\"}');",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = alert;
        await command.ExecuteNonQueryAsync();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
