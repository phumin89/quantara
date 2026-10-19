using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

namespace Quantara.MarketData.Checks;

internal sealed class AdaptiveTimeframeHandler(DateTimeOffset now, IReadOnlySet<string>? unavailable = null)
    : HttpMessageHandler
{
    private static readonly string[] symbols = ["BTC", "ETH", "XRP"];

    private readonly DateTimeOffset clock = now;

    public ConcurrentQueue<Uri> Requests { get; } = new();

    private readonly IReadOnlySet<string> unavailable = unavailable ?? new HashSet<string>(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException("Adaptive fixture received a non-GET request.");
        }

        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
        this.Requests.Enqueue(uri);
        await Task.Delay(uri.Query.Contains("symbol=ETH", StringComparison.Ordinal) ? 15 : 1, cancellationToken);
        if (uri.AbsolutePath.EndsWith("symbols", StringComparison.Ordinal))
        {
            return Response(Metadata());
        }

        if (uri.AbsolutePath.EndsWith("depth", StringComparison.Ordinal))
        {
            return Response("{\"code\":0,\"result\":{\"bids\":[[10,1]],\"asks\":[[11,1]]}}");
        }

        var query = Parse(uri.Query);
        var symbol = query["symbol"]?.Split('_')[0] ?? "BTC";
        var minutes = int.Parse(query["resolution"] ?? "5", CultureInfo.InvariantCulture);
        if (
            this.unavailable.Contains(symbol)
            || symbol == "ETH" && minutes == 5
            || symbol == "XRP" && minutes is 5 or 15
        )
        {
            return Response("{\"s\":\"no_data\"}");
        }

        var interval = minutes * 60;
        var to = query.TryGetValue("to", out var requestedTo)
            ? long.Parse(requestedTo, CultureInfo.InvariantCulture)
            : this.clock.ToUnixTimeSeconds() / interval * interval;
        return Response(Bars(interval, 121, to));
    }

    private static Dictionary<string, string> Parse(string query) =>
        query
            .TrimStart('?')
            .Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.ElementAtOrDefault(1) ?? "", StringComparer.Ordinal);

    private static string Metadata() =>
        "{\"code\":0,\"result\":["
        + string.Join(
            ',',
            symbols.Select(symbol =>
                $"{{\"base_asset\":\"{symbol}\",\"quote_asset\":\"THB\",\"status\":\"active\",\"market_segment\":\"SPOT\",\"source\":\"exchange\",\"min_quote_size\":1,\"price_step\":0.01,\"price_scale\":2,\"base_asset_scale\":8,\"quote_asset_scale\":2,\"freeze_buy\":false,\"freeze_sell\":false}}"
            )
        )
        + "]}";

    private static string Bars(int interval, int count, long to)
    {
        var times = string.Join(
            ',',
            Enumerable.Range(0, count).Select(i => (to - (120 - i) * interval).ToString(CultureInfo.InvariantCulture))
        );
        var values = string.Join(',', Enumerable.Repeat("10", count));
        return $"{{\"s\":\"ok\",\"t\":[{times}],\"o\":[{values}],\"h\":[{values}],\"l\":[{values}],\"c\":[{values}],\"v\":[{values}]}}";
    }

    private static HttpResponseMessage Response(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
