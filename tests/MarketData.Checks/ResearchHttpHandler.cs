using System.Net;

namespace Quantara.MarketData.Checks;

public sealed class ResearchHttpHandler : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    public Action<Uri, CancellationToken>? BeforeResponse { get; init; }

    public string? SymbolsResponse { get; init; }

    public string? TickerResponse { get; init; }

    public string? HistoryResponse { get; init; }

    public string? IntradayHistoryResponse { get; init; }

    public string? DepthResponse { get; init; }

    public string? TradesResponse { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing fixture request URI.");
        this.Requests.Add(uri);
        this.BeforeResponse?.Invoke(uri, cancellationToken);
        var path = uri.AbsolutePath;
        var query = uri.Query;
        var body =
            path.EndsWith("symbols", StringComparison.Ordinal)
                ? "{\"error\":0,\"result\":[{\"base_asset\":\"ETH\",\"quote_asset\":\"THB\",\"status\":\"active\",\"market_segment\":\"SPOT\",\"source\":\"exchange\",\"pairing_id\":\"1\",\"min_quote_size\":10,\"price_step\":0.1,\"price_scale\":1,\"base_asset_scale\":6,\"quote_asset_scale\":2,\"freeze_buy\":false,\"freeze_sell\":true},{\"base_asset\":\"X\",\"quote_asset\":\"USDT\",\"status\":\"active\",\"market_segment\":\"SPOT\",\"source\":\"exchange\"}]}"
            : path.EndsWith("ticker", StringComparison.Ordinal)
                ? "[{\"symbol\":\"ETH_THB\",\"last\":2,\"highest_bid\":1.9,\"lowest_ask\":2.1,\"quote_volume\":100,\"high_24_hr\":3,\"low_24_hr\":1,\"percent_change\":1}]"
            : path.EndsWith("depth", StringComparison.Ordinal)
            && query.Contains("sym=ETH_THB")
            && query.Contains("lmt=5")
                ? "{\"error\":0,\"asks\":[],\"bids\":[]}"
            : path.EndsWith("trades", StringComparison.Ordinal)
            && query.Contains("sym=ETH_THB")
            && query.Contains("lmt=10")
                ? "[{\"timestamp\":1,\"price\":2,\"amount\":1}]"
            : "{\"error\":11}";
        if (path.EndsWith("symbols", StringComparison.Ordinal) && this.SymbolsResponse is not null)
        {
            body = this.SymbolsResponse;
        }

        if (path.EndsWith("ticker", StringComparison.Ordinal) && this.TickerResponse is not null)
        {
            body = this.TickerResponse;
        }

        if (path.EndsWith("history", StringComparison.Ordinal) && this.HistoryResponse is not null)
        {
            body = this.HistoryResponse;
        }

        if (
            path.EndsWith("history", StringComparison.Ordinal)
            && query.Contains("resolution=5", StringComparison.Ordinal)
            && this.IntradayHistoryResponse is not null
        )
        {
            body = this.IntradayHistoryResponse;
        }

        if (path.EndsWith("depth", StringComparison.Ordinal) && this.DepthResponse is not null)
        {
            body = this.DepthResponse;
        }

        if (path.EndsWith("trades", StringComparison.Ordinal) && this.TradesResponse is not null)
        {
            body = this.TradesResponse;
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
