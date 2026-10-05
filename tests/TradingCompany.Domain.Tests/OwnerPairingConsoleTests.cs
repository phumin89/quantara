using System.IO.Pipes;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class OwnerPairingConsoleTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task Local_client_disconnect_does_not_stop_host_and_new_code_revokes_previous_session()
    {
        var sessions = new OwnerSession(new Clock());
        var pipeName = "quantara-test-" + Guid.NewGuid().ToString("N");
        using var host = new OwnerPairingConsole(sessions, pipeName);
        await host.StartAsync(CancellationToken.None);
        try
        {
            var first = await OwnerPairingConsole.RequestCodeAsync(pipeName);
            Assert.Equal(64, first.Length);
            var paired = sessions.TryPair(first);
            Assert.Equal(204, paired.Status);
            Assert.True(sessions.IsActive(paired.SessionId));
            var second = await OwnerPairingConsole.RequestCodeAsync(pipeName);
            Assert.False(sessions.IsActive(paired.SessionId));
            Assert.Equal(401, sessions.TryPair(first).Status);
            Assert.Equal(204, sessions.TryPair(second).Status);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Invalid_local_protocol_does_not_issue_code_or_revoke_session()
    {
        var sessions = new OwnerSession(new Clock());
        var paired = sessions.TryPair(sessions.IssuePairingCode());
        var pipeName = "quantara-test-" + Guid.NewGuid().ToString("N");
        using var host = new OwnerPairingConsole(sessions, pipeName);
        await host.StartAsync(CancellationToken.None);
        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(deadline.Token);
            await client.WriteAsync(new byte[] { 0 }, deadline.Token);
            Assert.Equal(0, await client.ReadAsync(new byte[64], deadline.Token));
            Assert.True(sessions.IsActive(paired.SessionId));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
}
