using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Checks;

public sealed class RiskEvidenceHttpHandler(RiskEvidenceFixtureMode mode = RiskEvidenceFixtureMode.Complete)
    : HttpMessageHandler
{
    private const string timestamp = "1700000000000";

    private const string secret = "risk-secret";

    private int balanceCalls;

    public RiskEvidenceFixtureMode Mode { get; } = mode;

    public ConcurrentDictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException("Risk evidence must never send a mutation.");
        }

        var path = request.RequestUri?.PathAndQuery ?? throw new InvalidOperationException("Missing fixture path.");
        this.Calls.AddOrUpdate(path.Split('?')[0], 1, (_, count) => count + 1);
        if (path == "/api/v3/servertime")
        {
            return Task.FromResult(Json(timestamp));
        }

        var signed = path != "/api/v3/market/symbols";
        if (signed)
        {
            var expected = Convert
                .ToHexString(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(secret),
                        Encoding.UTF8.GetBytes(timestamp + "GET" + path)
                    )
                )
                .ToLowerInvariant();
            if (request.Headers.GetValues("X-BTK-SIGN").Single() != expected)
            {
                throw new InvalidOperationException("Risk evidence signature mismatch.");
            }
        }

        var body = path switch
        {
            "/api/v4/wallet/balances" => this.Balances(),
            "/api/v3/market/symbols" => Symbols(),
            _ when path.StartsWith("/api/v3/market/my-open-orders", StringComparison.Ordinal) => Orders(),
            _ when path.StartsWith("/api/v4/fiat/deposit/history", StringComparison.Ordinal) => this.Mode
            == RiskEvidenceFixtureMode.InvalidFiatPagination
                ? FiatDepositWithInvalidPage()
                : FiatDeposit(),
            _ when path.StartsWith("/api/v4/fiat/withdraw/history", StringComparison.Ordinal) => this.FiatWithdrawal(),
            _ when path.StartsWith("/api/v4/crypto/deposits", StringComparison.Ordinal) => CryptoDeposit(),
            _ when path.StartsWith("/api/v4/crypto/withdraws", StringComparison.Ordinal) => CryptoWithdrawal(),
            _ => throw new InvalidOperationException("Unexpected risk evidence endpoint: " + path),
        };
        return Task.FromResult(Json(body));
    }

    private string Balances()
    {
        this.balanceCalls++;
        var btc = this.Mode == RiskEvidenceFixtureMode.ChangingFinalBalance && this.balanceCalls > 1 ? "1.1" : "1";
        return $"{{\"code\":\"0\",\"data\":[{{\"currency\":\"THB\",\"total\":\"5000\",\"available\":\"4000\"}},{{\"currency\":\"BTC\",\"total\":\"{btc}\",\"available\":\"1\"}}]}}";
    }

    private static string Symbols() =>
        "{\"error\":0,\"result\":["
        + "{\"symbol\":\"btc_thb\",\"market_segment\":\"SPOT\",\"source\":\"exchange\",\"status\":\"active\"},"
        + "{\"symbol\":\"eth_thb\",\"market_segment\":\"SPOT\",\"source\":\"exchange\",\"status\":\"active\"}]}";

    private static string Orders()
    {
        return $"{{\"error\":0,\"result\":[{{\"id\":\"same-order-id\",\"side\":\"buy\",\"amount\":\"100\",\"rate\":\"100\"}}]}}";
    }

    private static string FiatDeposit() =>
        "{\"code\":\"0\",\"data\":[{\"txn_id\":\"fiat-deposit-1\",\"currency\":\"THB\",\"amount\":\"1000\",\"status\":\"complete\",\"time\":1699999000}],\"pagination\":{\"page\":1,\"limit\":100}}";

    private string FiatWithdrawal() =>
        this.Mode == RiskEvidenceFixtureMode.MissingWithdrawalFee
            ? "{\"code\":\"0\",\"data\":[{\"txn_id\":\"fiat-withdraw-1\",\"currency\":\"THB\",\"amount\":\"200\",\"status\":\"complete\",\"time\":1699999000}],\"pagination\":{\"page\":1,\"limit\":100}}"
            : "{\"code\":\"0\",\"data\":[{\"txn_id\":\"fiat-withdraw-1\",\"currency\":\"THB\",\"amount\":\"200\",\"fee\":\"10\",\"status\":\"complete\",\"time\":1699999000}],\"pagination\":{\"page\":1,\"limit\":100}}";

    private static string CryptoDeposit() =>
        "{\"code\":\"0\",\"data\":{\"page\":1,\"total_page\":1,\"items\":[{\"hash\":\"deposit-hash-1\",\"symbol\":\"btc\",\"amount\":\"0.25\",\"status\":\"complete\",\"completed_at\":\"2023-11-14T22:13:20Z\"}]}}";

    private static string CryptoWithdrawal() =>
        "{\"code\":\"0\",\"data\":{\"page\":1,\"total_page\":1,\"items\":[{\"txn_id\":\"withdraw-txn-1\",\"symbol\":\"eth\",\"amount\":\"0.5\",\"fee\":\"0.01\",\"status\":\"complete\",\"completed_at\":\"2023-11-14T22:13:20Z\"}]}}";

    private static string FiatDepositWithInvalidPage() =>
        "{\"code\":\"0\",\"data\":[],\"pagination\":{\"page\":2,\"limit\":100}}";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
