using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class CurrentAccountReader(HttpClient http) : ICurrentAccountReader
{
    public async Task<string> Read(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("api/crypto/account", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
        var account = document.RootElement.GetProperty("accountKey").GetString();
        return account is not null && account.Length == 64 && account.All(Uri.IsHexDigit)
            ? account
            : throw new InvalidOperationException("Verified account context is unavailable.");
    }
}
