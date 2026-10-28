using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Checks;

public sealed class InspectionHttpHandler : HttpMessageHandler
{
    public string ClientId { get; set; } = "";

    public string Status { get; set; } = "filled";

    public bool Partial { get; set; }

    public bool Match { get; set; } = true;

    public bool FailTime { get; set; }

    public string? HistoryJson { get; set; }

    public string OrderAmountJson { get; set; } = "\"100\"";

    public string OrderRateJson { get; set; } = "100";

    public string OrderFilledJson { get; set; } = "0";

    public string OrderRemainingJson { get; set; } = "0";

    public bool LinkedChild { get; set; }

    public bool WrongChildIdentity { get; set; }

    public int OrderInfoReads { get; private set; }

    public int Posts { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var path = request.RequestUri?.PathAndQuery ?? throw new InvalidOperationException("Missing fixture path.");
        if (request.Method == HttpMethod.Post)
        {
            this.Posts += 1;
            throw new InvalidOperationException("Reconciliation must never POST to Bitkub.");
        }

        if (path == "/api/v3/servertime")
        {
            if (this.FailTime)
            {
                throw new HttpRequestException("Fixture servertime failure.");
            }

            return Task.FromResult(InspectionHttpHandler.Json("1701251212273"));
        }

        var expected = Convert
            .ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes("fake-secret"),
                    Encoding.UTF8.GetBytes("1701251212273GET" + path)
                )
            )
            .ToLowerInvariant();
        if (request.Headers.GetValues("X-BTK-SIGN").Single() != expected)
        {
            throw new InvalidOperationException("Wrong inspection GET signature.");
        }

        var client = this.Match ? this.ClientId : "different-client";
        var history = this.HistoryJson is null ? "" : ",\"history\":" + this.HistoryJson;
        var linkedChild = this.LinkedChild && path.Contains("&id=order-child&", StringComparison.Ordinal);
        var orderId = linkedChild ? "order-child" : "order-1";
        if (linkedChild && this.WrongChildIdentity)
        {
            orderId = "unrelated-order";
        }

        var amount = linkedChild ? "\"25\"" : this.OrderAmountJson;
        var first = this.LinkedChild ? ",\"first\":\"order-1\"" : "";
        if (path.StartsWith("/api/v3/market/order-info", StringComparison.Ordinal))
        {
            this.OrderInfoReads += 1;
        }

        var body =
            path.StartsWith("/api/v3/market/order-info", StringComparison.Ordinal)
                ? $"{{\"error\":0,\"result\":{{\"id\":\"{orderId}\"{first},\"client_id\":\"{client}\",\"amount\":{amount},\"rate\":{this.OrderRateJson},\"filled\":{this.OrderFilledJson},\"remaining\":{this.OrderRemainingJson},\"status\":\"{this.Status}\",\"partial_filled\":{this.Partial.ToString().ToLowerInvariant()}{history}}}}}"
            : path.StartsWith("/api/v3/market/my-open-orders", StringComparison.Ordinal)
                ? $"{{\"error\":0,\"result\":[{{\"id\":\"order-1\",\"side\":\"buy\",\"client_id\":\"{client}\"}}]}}"
            : "{\"error\":0,\"result\":[],\"pagination\":{\"has_next\":false}}";
        return Task.FromResult(InspectionHttpHandler.Json(body));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
