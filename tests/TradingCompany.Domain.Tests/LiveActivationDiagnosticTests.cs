using Npgsql;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveActivationDiagnosticTests
{
    [Fact]
    public void Account_lock_timeout_identifies_phase_without_echoing_private_exception_text()
    {
        var error = new NpgsqlException("Password=private-value; token=private-token",
            new TimeoutException("authenticated-url-private"));

        var message = LiveOwnerActivation.CommandFailure(LiveOwnerActivation.ActivationPhase.Activation, error);

        Assert.Equal("LIVE_NOT_ACTIVATED: Activation / STORAGE_TIMEOUT. No order was sent by this command.", message);
        Assert.DoesNotContain("private", message);
    }

    [Fact]
    public void Broker_stream_error_identifies_phase_without_raw_response_or_authentication()
    {
        var error = new System.Net.WebSockets.WebSocketException("api-secret-private");

        var message = LiveOwnerActivation.CommandFailure(LiveOwnerActivation.ActivationPhase.ReceiptStream, error);

        Assert.Equal("LIVE_NOT_ACTIVATED: ReceiptStream / BROKER_CONNECTION_UNAVAILABLE. No order was sent by this command.", message);
        Assert.DoesNotContain("private", message);
    }
}
