using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class PortfolioSnapshotReader : IPortfolioSnapshotReader
{
    private readonly HttpClient http;

    public PortfolioSnapshotReader(HttpClient http)
    {
        this.http = http;
    }

    public async Task<string> Read(CancellationToken cancellationToken)
    {
        using var response = await this.http.GetAsync("api/crypto/portfolio", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (
            root.GetProperty("updatedAt").ValueKind == JsonValueKind.Null
            || root.GetProperty("error").ValueKind != JsonValueKind.Null
            || root.GetProperty("syncing").GetBoolean()
        )
        {
            throw new InvalidOperationException(
                "A reconciled portfolio snapshot is unavailable. Refresh Crypto and retry."
            );
        }

        var positions = root.GetProperty("positions")
            .EnumerateArray()
            .Select(item => new
            {
                symbol = item.GetProperty("symbol").GetString(),
                quantity = item.GetProperty("quantity").GetDecimal(),
                currentPrice = PortfolioSnapshotReader.OptionalDecimal(item, "currentPrice"),
                value = PortfolioSnapshotReader.OptionalDecimal(item, "value"),
                averageCost = PortfolioSnapshotReader.OptionalDecimal(item, "averageCost"),
                costStatus = item.GetProperty("costStatus").GetString(),
            })
            .ToArray();
        var summary = root.GetProperty("summary");
        var json = JsonSerializer.Serialize(
            new
            {
                positions,
                summary = new
                {
                    totalAssets = PortfolioSnapshotReader.OptionalDecimal(summary, "totalAssets"),
                    availableThb = PortfolioSnapshotReader.OptionalDecimal(summary, "availableThb"),
                    holdingCount = PortfolioSnapshotReader.OptionalDecimal(summary, "holdingCount"),
                },
                updatedAt = root.GetProperty("updatedAt").Clone(),
                priceUpdatedAt = root.GetProperty("priceUpdatedAt").Clone(),
                historyUpdatedAt = root.GetProperty("historyUpdatedAt").Clone(),
                historyCoverage = root.GetProperty("historyCoverage").GetString(),
            }
        );
        if (json.Length > 60000)
        {
            throw new InvalidOperationException("Portfolio snapshot is too large.");
        }

        return json;
    }

    public async Task Refresh(CancellationToken cancellationToken)
    {
        using var response = await this.http.PostAsync("api/crypto/sync", null, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static decimal? OptionalDecimal(JsonElement value, string property)
    {
        var number = value.GetProperty(property);

        return number.ValueKind == JsonValueKind.Null ? null : number.GetDecimal();
    }
}
