using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Api.Domain;
using Quantara.Api.Infrastructure;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("Passed: " + name);
}

using var buy = JsonDocument.Parse("""{"txn_id":"trade-1","side":"buy","rate":"100","amount":"500","fee":"1.25","credit":"0.5","ts":1700000000000} """);
var buyTrade = BitkubReader.ParseTrade(buy.RootElement, "TEST");
Check(buyTrade.Quantity == 5 && buyTrade.Fee == 1.25m && buyTrade.FeeCredit == 0.5m, "buy uses quote amount and preserves fee/credit");
using var sell = JsonDocument.Parse("""{"txn_id":"trade-2","side":"sell","rate":"120","amount":"3","fee":"0.9","credit":"0","ts":1700000100000} """);
var sellTrade = BitkubReader.ParseTrade(sell.RootElement, "TEST");
Check(sellTrade.Quantity == 3, "sell uses base quantity");
var ledger = new PortfolioLedger { Balances = [new("TEST", 2)], Prices = [new("TEST", 120)] };
ledger = ledger.Remember(new("TEST", "Test coin"), [buyTrade, sellTrade, buyTrade]);
Check(ledger.Trades.Length == 2 && ledger.Coins.Length == 1, "reimport deduplicates transaction and coin");
var holding = PortfolioProjection.Positions(ledger).Single();
Check(holding.Quantity == 2 && holding.Value == 240 && holding.Status == "Holding", "reconciled balance drives holdings");
Check(holding.AverageCost is null && holding.RealizedPnl is null && holding.UnrealizedPnl is null, "partial history never invents cost or profit");
var closedLedger = ledger with { Balances = [], Prices = [] };
var closed = PortfolioProjection.Positions(closedLedger).Single();
Check(closed.Status == "Closed" && closed.Value == 0 && closed.Quantity == 0, "sold coin remains visible without a market price");
Check(PortfolioProjection.Positions(ledger with { Prices = [] }).Single().Value is null, "missing price never becomes zero holding value");

var exchangeHandler = new FakeReadHandler();
using var http = new HttpClient(exchangeHandler) { BaseAddress = new Uri("https://api.bitkub.com") };
var reader = new BitkubReader(http, new("test-key", "test-secret"));
var wallet = await reader.Balances(CancellationToken.None);
Check(wallet.Single(balance => balance.Symbol == "THB") is { Quantity: 1000, Available: 700 }, "wallet retains available THB separately from reserved cash");
var summaryLedger = ledger with { UpdatedAt = DateTimeOffset.UnixEpoch, Balances = wallet };
var summary = PortfolioProjection.Summary(summaryLedger);
Check(summary.TotalAssets == 240 && summary.AvailableThb == 700 && summary.HoldingCount == 1, "summary excludes THB from crypto value and coin count");
Check(PortfolioProjection.Summary(summaryLedger with { Coins = [] }).TotalAssets == 240, "summary includes unremembered positive wallet balances");
Check(PortfolioProjection.Summary(summaryLedger with { Prices = [] }).TotalAssets is null, "summary never reports a partial valuation as a total");
Check(PortfolioProjection.Summary(new PortfolioLedger()) is { TotalAssets: null, AvailableThb: null, HoldingCount: null }, "initial summary remains unknown before wallet sync");
Check(PortfolioProjection.Summary(summaryLedger with { Balances = [new("THB", 1000, 700)] }) is { TotalAssets: 0, HoldingCount: 0 }, "cash-only wallet has no held crypto value");
var legacyBalance = JsonSerializer.Deserialize<Balance>("""{"Symbol":"THB","Quantity":1000}""")!;
Check(legacyBalance.Available is null && PortfolioProjection.Summary(summaryLedger with { Balances = [legacyBalance] }).AvailableThb is null, "legacy payload never substitutes total cash for available cash");
exchangeHandler.InvalidWallet = true;
var rejectedWallet = false;
try { await reader.Balances(CancellationToken.None); }
catch (InvalidDataException) { rejectedWallet = true; }
Check(rejectedWallet, "wallet rejects available cash exceeding the total");
var history = await reader.History("TEST", CancellationToken.None);
Check(history.Length == 2 && exchangeHandler.HistoryCalls == 2, "history follows keyset pagination");
Check(exchangeHandler.VerifiedSignatures == 2, "signed GET uses exact timestamp, path and query");

if (args.Contains("--sql"))
{
    var account = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Quantara deterministic checks account"))).ToLowerInvariant();
    const string connection = "Server=(localdb)\\MSSQLLocalDB;Database=Quantara;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;";
    var first = new SqlPortfolioStore(connection, account);
    await first.Initialize(true, CancellationToken.None);
    await first.Save(closedLedger, CancellationToken.None);
    var second = new SqlPortfolioStore(connection, account);
    var restored = await second.Load(CancellationToken.None);
    Check(restored.Coins.Length == 1 && restored.Trades.Length == 2 && PortfolioProjection.Positions(restored).Single().Status == "Closed", "SQL Server retains closed coin and fills across store instances");
}

sealed class FakeReadHandler : HttpMessageHandler
{
    public bool InvalidWallet { get; set; }
    public int HistoryCalls { get; private set; }
    public int VerifiedSignatures { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Read adapter tried a mutation.");
        var path = request.RequestUri!.PathAndQuery;
        var json = "1700000000000";
        if (path.StartsWith("/api/v3/market/my-order-history", StringComparison.Ordinal))
        {
            HistoryCalls++;
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes("1700000000000GET" + path))).ToLowerInvariant();
            if (request.Headers.GetValues("X-BTK-SIGN").Single() != expected) throw new InvalidOperationException("Signature mismatch");
            VerifiedSignatures++;
            json = HistoryCalls == 1
                ? """{"error":0,"result":[{"txn_id":"a","side":"buy","rate":"10","amount":"20","fee":"0.05","credit":"0","ts":1700000000000}],"pagination":{"cursor":"next+page=","has_next":true}}"""
                : """{"error":0,"result":[{"txn_id":"b","side":"sell","rate":"12","amount":"2","fee":"0.06","credit":"0","ts":1700000100000}],"pagination":{"cursor":null,"has_next":false}}""";
            if (HistoryCalls == 2 && !path.Contains("cursor=next%2Bpage%3D")) throw new InvalidOperationException("Cursor was not encoded.");
        }
        else if (path == "/api/v4/wallet/balances")
            json = InvalidWallet
                ? """{"code":"0","data":[{"currency":"THB","total":"1000","available":"1001"}]}"""
                : """{"code":"0","data":[{"currency":"THB","total":"1000","available":"700","reserved":"300"},{"currency":"TEST","total":"2","available":"1","reserved":"1"}]}""";
        else if (path != "/api/v3/servertime") throw new InvalidOperationException("Unexpected endpoint.");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
