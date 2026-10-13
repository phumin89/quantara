using System.Text.Encodings.Web;
using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class PortfolioSnapshotReader : IPortfolioSnapshotReader
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
        var histories = await this.ReadHistories(
            positions
                .Select(item => item.symbol)
                .OfType<string>()
                .Where(symbol => symbol.Length is > 0 and <= 30 && symbol.All(char.IsAsciiLetterOrDigit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10),
            cancellationToken
        );
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
                    currency = "THB",
                    totalAssetsDefinition = "Market value of coin positions only; available THB cash is reported separately and is excluded.",
                    availableThbDefinition = "Available spendable THB in the connected Bitkub account; excludes reserved THB and is not total cash.",
                    holdingCountDefinition = "Number of non-THB coins with a positive wallet quantity.",
                },
                price = new
                {
                    currency = "THB",
                    source = "Bitkub",
                    meaning = "Latest imported ticker price; it is not an execution quote and does not include spread or fees.",
                },
                history = histories,
                historySymbolsTruncated = positions.Length > 10,
                pairDefinition = "Each coin symbol is quoted against THB (for example BTC/THB).",
                updatedAt = root.GetProperty("updatedAt").Clone(),
                priceUpdatedAt = root.GetProperty("priceUpdatedAt").Clone(),
                historyUpdatedAt = root.GetProperty("historyUpdatedAt").Clone(),
                historyCoverage = root.GetProperty("historyCoverage").GetString(),
            },
            PortfolioSnapshotReader.jsonOptions
        );
        if (json.Length > 40000)
        {
            throw new InvalidOperationException("Portfolio snapshot is too large.");
        }

        return json;
    }

    private async Task<object[]> ReadHistories(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        var histories = new List<object>();
        foreach (var symbol in symbols)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var response = await this.http.GetAsync(
                    $"api/crypto/history?coin={Uri.EscapeDataString(symbol)}",
                    timeout.Token
                );
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
                var root = document.RootElement;
                var sourceTrades = root.GetProperty("trades")
                    .EnumerateArray()
                    .Take(20)
                    .Select(PortfolioSnapshotReader.ToTradeEvidence)
                    .ToArray();
                histories.Add(
                    new
                    {
                        symbol,
                        available = true,
                        coverage = root.GetProperty("coverage").GetString(),
                        returnedTradeCount = sourceTrades.Length,
                        truncated = root.GetProperty("trades").GetArrayLength() > sourceTrades.Length,
                        trades = sourceTrades,
                    }
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                histories.Add(
                    new
                    {
                        symbol,
                        available = false,
                        coverage = (string?)null,
                        returnedTradeCount = 0,
                        truncated = false,
                        trades = Array.Empty<object>(),
                    }
                );
            }
        }

        return histories.ToArray();
    }

    private static object ToTradeEvidence(JsonElement item)
    {
        return new
        {
            transactionId = item.GetProperty("transactionId").GetString(),
            symbol = item.GetProperty("symbol").GetString(),
            side = item.GetProperty("side").GetString(),
            quantity = item.GetProperty("quantity").GetDecimal(),
            price = item.GetProperty("price").GetDecimal(),
            fee = item.GetProperty("fee").GetDecimal(),
            feeCredit = item.GetProperty("feeCredit").GetDecimal(),
            timestamp = item.GetProperty("timestamp").Clone(),
        };
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
