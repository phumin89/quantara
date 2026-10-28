using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class CurrentAccountReader(HttpClient http) : ICurrentAccountReader
{
    public async Task<string> Read(CancellationToken cancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(http.Timeout);
        using var response = await http.GetAsync(
            "api/crypto/account",
            HttpCompletionOption.ResponseHeadersRead,
            requestTimeout.Token
        );
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(16000, requestTimeout.Token);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(requestTimeout.Token),
            cancellationToken: requestTimeout.Token
        );
        var account = document.RootElement.GetProperty("accountKey").GetString();
        requestTimeout.Token.ThrowIfCancellationRequested();
        return account is not null && account.Length == 64 && account.All(Uri.IsHexDigit)
            ? account
            : throw new InvalidOperationException("Verified account context is unavailable.");
    }
}
