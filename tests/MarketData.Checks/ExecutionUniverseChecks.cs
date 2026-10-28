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

        var extremeRules = new[]
        {
            new
            {
                base_asset = "EXTREME",
                quote_asset = "THB",
                status = "active",
                market_segment = "SPOT",
                source = "exchange",
                pairing_id = "900",
                freeze_buy = false,
                freeze_sell = false,
            },
            new
            {
                base_asset = "HEALTHY",
                quote_asset = "THB",
                status = "active",
                market_segment = "SPOT",
                source = "exchange",
                pairing_id = "901",
                freeze_buy = false,
                freeze_sell = false,
            },
        };
        var extremeHandler = new UniverseHttpHandler
        {
            SymbolsResponse = JsonSerializer.Serialize(new { result = extremeRules }),
            TickerResponse = JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        symbol = "EXTREME_THB",
                        last = 0.0001m,
                        highest_bid = 0.00009m,
                        lowest_ask = 0.00011m,
                        quote_volume = 1m,
                        high_24_hr = decimal.MaxValue,
                        low_24_hr = 0.00001m,
                        percent_change = 1m,
                    },
                    new
                    {
                        symbol = "HEALTHY_THB",
                        last = 100m,
                        highest_bid = 99m,
                        lowest_ask = 101m,
                        quote_volume = 100m,
                        high_24_hr = 101m,
                        low_24_hr = 99m,
                        percent_change = 1m,
                    },
                }
            ),
        };
        using var extremeClient = new HttpClient(extremeHandler) { BaseAddress = new Uri("https://test.invalid") };
        using var extreme = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(extremeClient, timeProvider: time).ReadUniverse(
                CancellationToken.None
            )
        );
        if (
            extreme.RootElement.GetProperty("symbols").EnumerateArray().Any(item => item.GetString() == "EXTREME")
            || !extreme.RootElement.GetProperty("symbols").EnumerateArray().Any(item => item.GetString() == "HEALTHY")
        )
        {
            throw new InvalidOperationException(
                "Extreme derived metrics must be unusable without dropping healthy markets."
            );
        }

        using var extremeSymbols = JsonDocument.Parse(extremeHandler.SymbolsResponse);
        using var extremeQuotes = JsonDocument.Parse(extremeHandler.TickerResponse);
        var extremeEntry = MarketUniverseScanner
            .Scan(extremeSymbols.RootElement, extremeQuotes.RootElement)
            .Entries.Single(entry => entry.Symbol == "EXTREME_THB");
        if (extremeEntry.Usable || extremeEntry.Range24h is not null || extremeEntry.SpreadPercent is not null)
        {
            throw new InvalidOperationException("Unrepresentable quote metrics must remain unknown, not zero.");
        }

        var malformedHandler = new UniverseHttpHandler
        {
            SymbolsResponse =
                "{\"result\":[null,42,{\"base_asset\":\"HEALTHY\",\"quote_asset\":\"THB\",\"status\":\"active\",\"market_segment\":\"SPOT\",\"source\":\"exchange\",\"pairing_id\":\"1\",\"freeze_buy\":false,\"freeze_sell\":false}]}",
            TickerResponse = JsonSerializer.Serialize(
                new object?[]
                {
                    null,
                    42,
                    new
                    {
                        symbol = "HEALTHY_THB",
                        last = 100m,
                        highest_bid = 99m,
                        lowest_ask = 101m,
                        quote_volume = 100m,
                        high_24_hr = 101m,
                        low_24_hr = 99m,
                        percent_change = 1m,
                    },
                }
            ),
        };
        using var malformedClient = new HttpClient(malformedHandler) { BaseAddress = new Uri("https://test.invalid") };
        using var malformed = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(malformedClient, timeProvider: time).ReadUniverse(
                CancellationToken.None
            )
        );
        if (!malformed.RootElement.GetProperty("symbols").EnumerateArray().Any(item => item.GetString() == "HEALTHY"))
        {
            throw new InvalidOperationException("Malformed per-row metadata must not discard healthy markets.");
        }
    }
}
