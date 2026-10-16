using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class ExecutionUniverseChecks
{
    public static async Task Run()
    {
        var rules = Enumerable
            .Range(1, 19)
            .Select(index => new
            {
                base_asset = "C" + index,
                quote_asset = "THB",
                status = "active",
                market_segment = "SPOT",
                source = "exchange",
                pairing_id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                freeze_buy = index > 17,
                freeze_sell = false,
            })
            .ToArray();
        var quotes = rules.Select(rule => new
        {
            symbol = rule.base_asset + "_THB",
            last = 100,
            highest_bid = 99,
            lowest_ask = 101,
            quote_volume = 100,
            high_24_hr = 101,
            low_24_hr = 99,
            percent_change = 1,
        });
        var handler = new UniverseHttpHandler
        {
            SymbolsResponse = JsonSerializer.Serialize(new { result = rules }),
            TickerResponse = JsonSerializer.Serialize(quotes),
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var reader = new BitkubExecutionMarketReader(client, timeProvider: time);
        using var document = JsonDocument.Parse(await reader.ReadUniverse(CancellationToken.None));
        var root = document.RootElement;
        if (
            root.GetProperty("symbols").GetArrayLength() != 17
            || root.GetProperty("excludedMarkets").GetInt32() != 2
            || root.GetProperty("error").ValueKind != JsonValueKind.Null
        )
        {
            throw new InvalidOperationException("Execution universe must return every usable market.");
        }

        var observedAt = root.GetProperty("observedAt").GetDateTimeOffset();
        using var cached = JsonDocument.Parse(await reader.ReadUniverse(CancellationToken.None));
        if (handler.RequestCount != 2 || cached.RootElement.GetProperty("observedAt").GetDateTimeOffset() != observedAt)
        {
            throw new InvalidOperationException("Successful universe scans must preserve the cached observation.");
        }

        var invalidHandler = new UniverseHttpHandler
        {
            SymbolsResponse = handler.SymbolsResponse,
            TickerResponse = "{\"error\":0}",
        };
        using var invalidClient = new HttpClient(invalidHandler) { BaseAddress = new Uri("https://test.invalid") };
        using var invalid = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(invalidClient, timeProvider: time).ReadUniverse(
                CancellationToken.None
            )
        );
        if (invalid.RootElement.GetProperty("symbols").GetArrayLength() != 0)
        {
            throw new InvalidOperationException("Invalid ticker must fail closed without stale eligibility.");
        }

        var failedHandler = new UniverseHttpHandler
        {
            SymbolsResponse = handler.SymbolsResponse,
            TickerResponse = handler.TickerResponse,
            StatusCode = System.Net.HttpStatusCode.InternalServerError,
        };
        using var failedClient = new HttpClient(failedHandler) { BaseAddress = new Uri("https://test.invalid") };
        using var failed = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(failedClient, timeProvider: time).ReadUniverse(CancellationToken.None)
        );
        if (failed.RootElement.GetProperty("symbols").GetArrayLength() != 0)
        {
            throw new InvalidOperationException("HTTP failure must return an empty universe.");
        }
    }
}
