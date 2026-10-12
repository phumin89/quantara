using System.Security.Cryptography;
using System.Text;

internal sealed class FakeReadHandler : HttpMessageHandler
{
    public bool InvalidWallet { get; set; }
    public int HistoryCalls { get; private set; }
    public int VerifiedSignatures { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException("Read adapter tried a mutation.");
        }

        var path = (request.RequestUri ?? throw new InvalidOperationException("Request URI is missing.")).PathAndQuery;
        var json = "1700000000000";
        if (path.StartsWith("/api/v3/market/my-order-history", StringComparison.Ordinal))
        {
            this.HistoryCalls++;
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes("1700000000000GET" + path))).ToLowerInvariant();
            if (request.Headers.GetValues("X-BTK-SIGN").Single() != expected)
            {
                throw new InvalidOperationException("Signature mismatch");
            }

            this.VerifiedSignatures++;
            json = this.HistoryCalls == 1
                ? """{"error":0,"result":[{"txn_id":"a","side":"buy","rate":"10","amount":"20","fee":"0.05","credit":"0","ts":1700000000000}],"pagination":{"cursor":"next+page=","has_next":true}}"""
                : """{"error":0,"result":[{"txn_id":"b","side":"sell","rate":"12","amount":"2","fee":"0.06","credit":"0","ts":1700000100000}],"pagination":{"cursor":null,"has_next":false}}""";
            if (this.HistoryCalls == 2 && !path.Contains("cursor=next%2Bpage%3D"))
            {
                throw new InvalidOperationException("Cursor was not encoded.");
            }
        }
        else if (path == "/api/v4/wallet/balances")
        {
            json = this.InvalidWallet
                ? """{"code":"0","data":[{"currency":"THB","total":"1000","available":"1001"}]}"""
                : """{"code":"0","data":[{"currency":"THB","total":"1000","available":"700","reserved":"300"},{"currency":"TEST","total":"2","available":"1","reserved":"1"}]}""";
        }
        else if (path != "/api/v3/servertime")
        {
            throw new InvalidOperationException("Unexpected endpoint.");
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
