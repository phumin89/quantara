using Quantara.MarketData.Infrastructure;
using Quantara.MarketData.Checks;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var handler = new FakeBitkubHandler(
    "{\"code\":0,\"result\":[{\"base_asset\":\"BTC\",\"quote_asset\":\"THB\",\"name\":\"Bitcoin\",\"source\":\"exchange\"},{\"base_asset\":\"USD\",\"quote_asset\":\"USDT\",\"name\":\"Dollar\",\"source\":\"exchange\"}]}",
    "[{\"symbol\":\"BTC_THB\",\"last\":\"2813333.33\"},{\"symbol\":\"BTC_USDT\",\"last\":123},{\"symbol\":\"ETH_THB\",\"last\":0}]");
using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
var reader = new BitkubMarketDataReader(client);
var coins = await reader.ReadCoins(CancellationToken.None);
var quotes = await reader.ReadQuotes(CancellationToken.None);
Assert(coins.Count == 1 && coins[0].Symbol == "BTC" && coins[0].Name == "Bitcoin", "coin filtering failed");
Assert(quotes.Count == 1 && quotes[0].Symbol == "BTC" && quotes[0].Price == 2813333.33m, "quote filtering or string parsing failed");

var failed = new FakeBitkubHandler("{\"code\":11,\"result\":[]}", "[]");
using var failedClient = new HttpClient(failed) { BaseAddress = new Uri("https://test.invalid") };
var failedReader = new BitkubMarketDataReader(failedClient);
try
{
    await failedReader.ReadCoins(CancellationToken.None);
    throw new InvalidOperationException("provider error was accepted");
}
catch (InvalidDataException)
{
}

Console.WriteLine("MarketData checks passed.");
using var stringErrorClient = new HttpClient(new FakeBitkubHandler("{\"error\":\"11\",\"result\":[]}", "[]")) { BaseAddress = new Uri("https://test.invalid") };
var stringErrorReader = new BitkubMarketDataReader(stringErrorClient);
var rejectedError = false;
try
{
    await stringErrorReader.ReadCoins(CancellationToken.None);
}
catch (InvalidDataException)
{
    rejectedError = true;
}
Assert(rejectedError, "string provider error was accepted");
Console.WriteLine("String provider errors fail closed.");
