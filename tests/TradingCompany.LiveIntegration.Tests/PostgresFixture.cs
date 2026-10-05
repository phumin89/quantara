using System.Diagnostics;
using Npgsql;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string container = "quantara-live-test-" + Guid.NewGuid().ToString("N");
    private readonly string password = Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await Docker("run", "-d", "--name", container, "-p", "127.0.0.1::5432", "-e", "POSTGRES_PASSWORD", "-e", "POSTGRES_DB=quantara_live_test", "postgres:16-alpine");
        var port = (await Docker("port", container, "5432/tcp")).Trim().Split(':')[^1];
        ConnectionString = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = int.Parse(port), Database = "quantara_live_test", Username = "postgres", Password = password, Pooling = false, Timeout = 2 }.ToString();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try { await using var connection = new NpgsqlConnection(ConnectionString); await connection.OpenAsync(); return; }
            catch (NpgsqlException) when (attempt < 29) { await Task.Delay(500); }
        }
    }

    public async Task DisposeAsync() => await Docker("rm", "-f", container);
    public async Task StopAsync() => await Docker("stop", "--time", "1", container);

    private async Task<string> Docker(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["POSTGRES_PASSWORD"] = password;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker failed to start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new InvalidOperationException("Docker test fixture failed: " + await error);
        return await output;
    }
}

[CollectionDefinition("Live database")]
public sealed class LiveDatabaseCollection : ICollectionFixture<PostgresFixture>;
