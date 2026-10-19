using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace Portfolio.Checks;

public sealed class TradingHistoryHttpHandler : HttpMessageHandler
{
    public string? FailureMode { get; init; }

    public bool Empty { get; init; }

    public ConcurrentQueue<Uri> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException("History reader must use GET only.");
        }

        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing URI.");
        this.Requests.Enqueue(uri);
        var query = uri
            .Query.TrimStart('?')
            .Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        var symbol = query["symbol"];
        var timeframe = int.Parse(query["timeframeSeconds"], CultureInfo.InvariantCulture);
        var from = long.Parse(query["from"], CultureInfo.InvariantCulture);
        var to = long.Parse(query["to"], CultureInfo.InvariantCulture);
        var body = this.Build(symbol, timeframe, from, to);
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }
        );
    }

    private string Build(string symbol, int timeframe, long from, long to)
    {
        if (this.FailureMode == "Error")
        {
            return "{\"error\":\"history unavailable\"}";
        }

        var outputSymbol = this.FailureMode == "Symbol" ? "ETH" : symbol;
        var outputFrame = this.FailureMode == "Timeframe" ? timeframe * 3 : timeframe;
        var source = this.FailureMode == "Source" ? "other-source" : "bitkub-tradingview";
        if (this.Empty)
        {
            return Envelope(outputSymbol, outputFrame, from, to, "[]", "null");
        }

        var values = new List<string>();
        for (var timestamp = from; timestamp < to; timestamp += timeframe)
        {
            values.Add($"{{\"timestamp\":{timestamp},\"open\":10,\"high\":11,\"low\":9,\"close\":10,\"volume\":1}}");
        }

        if (this.FailureMode == "Duplicate" && values.Count > 1)
        {
            values[1] = values[0];
        }

        if (this.FailureMode == "OutOfRange" && values.Count > 0)
        {
            values[0] = values[0].Replace($"\"timestamp\":{from}", $"\"timestamp\":{to}", StringComparison.Ordinal);
        }

        if (this.FailureMode == "Malformed" && values.Count > 0)
        {
            values[0] = values[0].Replace("\"high\":11", "\"high\":1", StringComparison.Ordinal);
        }

        return $"{{\"symbol\":\"{outputSymbol}\",\"timeframeSeconds\":{outputFrame},\"source\":\"{source}\",\"from\":{from},\"to\":{to},\"observedAt\":\"2026-10-18T00:00:00Z\",\"candles\":[{string.Join(',', values)}],\"error\":null}}";
    }

    private static string Envelope(string symbol, int timeframe, long from, long to, string candles, string error) =>
        $"{{\"symbol\":\"{symbol}\",\"timeframeSeconds\":{timeframe},\"source\":\"bitkub-tradingview\",\"from\":{from},\"to\":{to},\"observedAt\":\"2026-10-18T00:00:00Z\",\"candles\":{candles},\"error\":{error}}}";
}
