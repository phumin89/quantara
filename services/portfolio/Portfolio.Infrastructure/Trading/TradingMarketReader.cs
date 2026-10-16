using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingMarketReader(HttpClient http) : ITradingMarketReader
{
    public async Task<IReadOnlyList<TradingBook>> Read(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    )
    {
        using var response = await http.GetAsync(
            "api/market/trading?symbols=" + Uri.EscapeDataString(string.Join(',', symbols)),
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > 1000000)
        {
            throw new InvalidDataException("Trading evidence exceeds its budget.");
        }

        using var document = JsonDocument.Parse(text);
        var rows = document.RootElement.GetProperty("markets");
        foreach (var row in rows.EnumerateArray())
        {
            foreach (
                var name in new[] { "minimumNotional", "priceStep", "priceScale", "baseScale", "quoteScale", "enabled" }
            )
            {
                if (!row.TryGetProperty(name, out _))
                {
                    throw new InvalidDataException("Required trading market rules are missing.");
                }
            }
        }

        return rows.Deserialize<TradingBook[]>(TradingJson.Options)
            ?? throw new InvalidDataException("Trading evidence is unavailable.");
    }
}
