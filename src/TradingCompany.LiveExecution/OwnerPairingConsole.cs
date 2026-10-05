using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace TradingCompany.LiveExecution;

// OS-local, same-user IPC only. No HTTP route or shared mount exposes this channel.
public sealed class OwnerPairingConsole(OwnerSession sessions, string pipeName = OwnerPairingConsole.PipeName) : BackgroundService
{
    public const string PipeName = "quantara-owner-pairing";
    private const byte ProtocolVersion = 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var request = new byte[1];
                if (await pipe.ReadAsync(request, deadline.Token) != 1 || request[0] != ProtocolVersion) continue;
                var response = Encoding.ASCII.GetBytes(sessions.IssuePairingCode());
                try
                {
                    await pipe.WriteAsync(response, deadline.Token);
                    await pipe.FlushAsync(deadline.Token);
                }
                finally { CryptographicOperations.ZeroMemory(response); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    public static async Task<string> RequestCodeAsync(string pipeName = PipeName, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token);
        await pipe.WriteAsync(new byte[] { ProtocolVersion }, deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        var response = new byte[64];
        try
        {
            await pipe.ReadExactlyAsync(response, deadline.Token);
            var code = Encoding.ASCII.GetString(response);
            if (code.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
                throw new InvalidDataException("Invalid local pairing response.");
            return code;
        }
        finally { CryptographicOperations.ZeroMemory(response); }
    }
}
