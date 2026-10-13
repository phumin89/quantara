using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class ResearchMarketChecks
{
    public static async Task Run()
    {
        var handler = new ResearchHttpHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        using var document = JsonDocument.Parse(
            await new BitkubResearchMarketReader(client).Read(CancellationToken.None)
        );
        var root = document.RootElement;
        var market = root.GetProperty("markets")[0];
        if (
            market.GetProperty("symbol").GetString() != "ETH_THB"
            || market.GetProperty("rules").GetProperty("priceStep").GetDecimal() != 0.1m
        )
        {
            throw new InvalidOperationException("research mapping failed");
        }

        if (
            !handler.Requests.Any(x => x.Query.Contains("sym=ETH_THB") && x.Query.Contains("lmt=5"))
            || !handler.Requests.Any(x => x.Query.Contains("sym=ETH_THB") && x.Query.Contains("lmt=10"))
        )
        {
            throw new InvalidOperationException("bounded depth/trades query failed");
        }

        using var candles = JsonDocument.Parse(
            "{\"s\":\"ok\",\"t\":[0,0,86400],\"o\":[10,10,10],\"h\":[12,12,12],\"l\":[9,9,9],\"c\":[11,11.5,11],\"v\":[1,1,1]}"
        );
        var parsed = CandleParser.Parse(candles.RootElement, 0, 172800);
        if (parsed.Count != 1 || parsed[0].Timestamp != 86400)
        {
            throw new InvalidOperationException("Conflicting candle timestamps must not invent a chosen close.");
        }

        if (CandleParser.Parse(candles.RootElement, 0, 86400).Count != 0)
        {
            throw new InvalidOperationException("Incomplete daily bars must be excluded.");
        }
    }
}
