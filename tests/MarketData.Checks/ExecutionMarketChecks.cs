using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Quantara.MarketData.Domain;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public sealed class ExecutionMarketChecks : HttpMessageHandler
{
    private static readonly string[] fixtureSymbols = ["BTC", "NEAR"];

    private readonly bool invalidDepth;

    private readonly bool timeoutDepth;

    private readonly string? metadata;

    private readonly int historyRows;

    private readonly int? missingHistoryIndex;

    private readonly bool unavailableHistory;

    private readonly ConcurrentQueue<Uri> requests = new();

    private ExecutionMarketChecks(
        bool invalidDepth = false,
        bool timeoutDepth = false,
        string? metadata = null,
        int historyRows = 121,
        int? missingHistoryIndex = null,
        bool unavailableHistory = false
    )
    {
        this.invalidDepth = invalidDepth;
        this.timeoutDepth = timeoutDepth;
        this.metadata = metadata;
        this.historyRows = historyRows;
        this.missingHistoryIndex = missingHistoryIndex;
        this.unavailableHistory = unavailableHistory;
    }

    public static async Task Run()
    {
        using var handler = new ExecutionMarketChecks();
        using var client = Client(handler);
        var reader = new BitkubExecutionMarketReader(
            client,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 10, 23, 4, 0, 0, TimeSpan.Zero))
        );
        using var result = JsonDocument.Parse(await reader.Read(["NEAR", "BTC"], CancellationToken.None));
        var markets = result.RootElement.GetProperty("markets");
        var near = markets[0];
        Check(
            markets.GetArrayLength() == 2 && near.GetProperty("symbol").GetString() == "NEAR",
            "Explicit outside-feed market was omitted."
        );
        Check(result.RootElement.GetProperty("errors").GetArrayLength() == 0, "Valid execution evidence failed.");
        Check(!near.TryGetProperty("sellEnabled", out _), "Default market contracts retain their legacy shape.");
        var bars = near.GetProperty("bars");
        var boundary = handler
            .requests.First(uri => uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal))
            .Query.Split('&')
            .Single(part => part.StartsWith("to=", StringComparison.Ordinal))[3..];
        Check(
            bars.GetArrayLength() == 120
                && bars[119].GetProperty("timestamp").GetInt64()
                    == long.Parse(boundary, CultureInfo.InvariantCulture) - 300,
            "Execution evidence must retain all 120 completed bars and exclude the inclusive open bar."
        );
        Check(
            bars[0].GetProperty("close").GetDecimal() == 10.123456789012345m
                && near.GetProperty("bid").GetDecimal() == 10.123456789012345m
                && near.GetProperty("bidQuantity").GetDecimal() == .123456789012345m
                && near.GetProperty("priceStep").GetDecimal() == .000000000000001m
                && near.GetProperty("priceScale").GetInt32() == 15,
            "Execution decimals or exchange precision lost source precision."
        );
        Check(
            !near.GetProperty("enabled").GetBoolean() && markets[1].GetProperty("enabled").GetBoolean(),
            "Either frozen side must disable the market."
        );
        using var second = JsonDocument.Parse(await reader.Read(["NEAR", "BTC"], CancellationToken.None));
        Check(
            handler.requests.Count(uri => uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal)) == 2
                && handler.requests.Count(uri => uri.AbsolutePath.EndsWith("depth", StringComparison.Ordinal)) == 4,
            "Completed history should be cached by candle slot while every order book stays fresh."
        );

        using var quotesOnly = JsonDocument.Parse(
            await reader.Read(["BTC"], CancellationToken.None, includeHistory: false)
        );
        Check(
            quotesOnly.RootElement.GetProperty("markets")[0].GetProperty("bars").GetArrayLength() == 0
                && quotesOnly.RootElement.GetProperty("markets")[0].GetProperty("sellEnabled").GetBoolean()
                && handler.requests.Count(uri => uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal)) == 2,
            "Exit quotes and account valuation must not wait for candle history."
        );

        foreach (
            var invalid in new IReadOnlyList<string>[]
            {
                [],
                [""],
                ["BTC", "BTC"],
                ["btc"],
                ["BTC_THB"],
                ["BTC&x=1"],
                ["บีทีซี"],
                Enumerable.Repeat("BTC", 17).ToArray(),
            }
        )
        {
            var rejected = false;
            try
            {
                await reader.Read(invalid, CancellationToken.None);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Check(rejected, "Invalid execution symbol input was accepted.");
        }

        using var invalidHandler = new ExecutionMarketChecks(invalidDepth: true);
        using var invalidClient = Client(invalidHandler);
        using var invalidResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(invalidClient).Read(
                ["NEAR", "BTC", "UNKNOWN"],
                CancellationToken.None
            )
        );
        Check(
            invalidResult.RootElement.GetProperty("markets").GetArrayLength() == 1
                && invalidResult.RootElement.GetProperty("markets")[0].GetProperty("symbol").GetString() == "BTC"
                && invalidResult.RootElement.GetProperty("errors").GetArrayLength() == 2,
            "Invalid/unknown market must return its own error without an invented book or suppressing a healthy market."
        );

        using var timeoutHandler = new ExecutionMarketChecks(timeoutDepth: true);
        using var timeoutClient = Client(timeoutHandler);
        timeoutClient.Timeout = TimeSpan.FromMilliseconds(30);
        using var timeoutResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(timeoutClient).Read(["NEAR", "BTC"], CancellationToken.None)
        );
        Check(
            timeoutResult.RootElement.GetProperty("markets").GetArrayLength() == 1
                && timeoutResult.RootElement.GetProperty("errors")[0].GetProperty("error").GetString()
                    == "Public market evidence timed out.",
            "Provider timeout must fail closed per symbol."
        );

        using var metadataHandler = new ExecutionMarketChecks(metadata: "{\"error\":11,\"result\":[]}");
        using var metadataClient = Client(metadataHandler);
        using var metadataResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(metadataClient).Read(["BTC", "NEAR"], CancellationToken.None)
        );
        Check(
            metadataResult.RootElement.GetProperty("markets").GetArrayLength() == 0
                && metadataResult.RootElement.GetProperty("errors").GetArrayLength() == 2,
            "Invalid metadata must not fabricate healthy market rules."
        );

        using var shortHandler = new ExecutionMarketChecks(historyRows: 52);
        using var shortClient = Client(shortHandler);
        using var shortResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(shortClient).Read(["BTC"], CancellationToken.None)
        );
        Check(
            shortResult.RootElement.GetProperty("markets")[0].GetProperty("bars").GetArrayLength() == 51,
            "A valid 51-bar outside-feed history must remain complete."
        );

        using var insufficientHandler = new ExecutionMarketChecks(historyRows: 51);
        using var insufficientClient = Client(insufficientHandler);
        using var insufficientResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(insufficientClient).Read(["BTC"], CancellationToken.None)
        );
        Check(
            insufficientResult.RootElement.GetProperty("markets").GetArrayLength() == 0
                && insufficientResult.RootElement.GetProperty("errors").GetArrayLength() == 1,
            "An insufficient completed warmup must not become usable execution evidence."
        );

        using var gappedHandler = new ExecutionMarketChecks(historyRows: 121);
        using var gappedClient = Client(gappedHandler);
        var fixedNow = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var fixedClock = new FixedTimeProvider(fixedNow);
        var gappedTo = fixedNow.ToUnixTimeSeconds() / 300 * 300;
        var gappedSnapshot = Snapshot(
            fixedNow,
            Enumerable
                .Range(0, 60)
                .Where(index => index != 10)
                .Select(index => new MarketCandle(gappedTo - (60 - index) * 300, 20, 21, 19, 20, 1))
                .ToArray()
        );
        using var repairedResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(
                gappedClient,
                new ResearchIntradayReader(gappedSnapshot),
                fixedClock
            ).Read(["BTC"], CancellationToken.None)
        );
        Check(
            gappedHandler.requests.Count(uri => uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal)) == 1
                && repairedResult.RootElement.GetProperty("markets")[0].GetProperty("bars").GetArrayLength() >= 51,
            "A cached gap with fewer than 51 contiguous recent bars must trigger fresh history repair."
        );

        using var suffixHandler = new ExecutionMarketChecks();
        using var suffixClient = Client(suffixHandler);
        var suffixNow = fixedNow;
        var suffixTo = suffixNow.ToUnixTimeSeconds() / 300 * 300;
        var suffixBars = Enumerable
            .Range(0, 70)
            .Where(index => index != 5)
            .Select(index => new MarketCandle(suffixTo - (70 - index) * 300, 20, 21, 19, 20, 1))
            .ToArray();
        using var suffixResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(
                suffixClient,
                new ResearchIntradayReader(Snapshot(suffixNow, suffixBars)),
                fixedClock
            ).Read(["BTC"], CancellationToken.None)
        );
        Check(
            !suffixHandler.requests.Any(uri => uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal))
                && suffixResult.RootElement.GetProperty("markets")[0].GetProperty("bars").GetArrayLength() == 64,
            "An older disconnected prefix must be ignored when the latest contiguous suffix is sufficient."
        );

        var suffixFirst = suffixTo - 64 * 300;
        Check(
            suffixResult
                .RootElement.GetProperty("markets")[0]
                .GetProperty("bars")[0]
                .GetProperty("timestamp")
                .GetInt64() == suffixFirst,
            "The retained contiguous suffix must preserve its first candle timestamp."
        );

        using var httpGapHandler = new ExecutionMarketChecks(missingHistoryIndex: 60);
        using var httpGapClient = Client(httpGapHandler);
        using var httpGapResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(httpGapClient, timeProvider: fixedClock).Read(
                ["BTC"],
                CancellationToken.None
            )
        );
        Check(
            httpGapResult.RootElement.GetProperty("markets")[0].GetProperty("bars").GetArrayLength() == 59,
            "A fresh HTTP history with an older gap must return its sufficient contiguous suffix."
        );

        using var unresolvedHandler = new ExecutionMarketChecks(missingHistoryIndex: 90);
        using var unresolvedClient = Client(unresolvedHandler);
        using var unresolvedResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(unresolvedClient, timeProvider: fixedClock).Read(
                ["BTC"],
                CancellationToken.None
            )
        );
        Check(
            unresolvedResult.RootElement.GetProperty("markets").GetArrayLength() == 0
                && unresolvedResult.RootElement.GetProperty("errors")[0].GetProperty("error").GetString()
                    == "Fewer than 51 consecutive recent completed candles could be verified.",
            "An unresolved recent history gap must fail closed."
        );

        using var missingLatestHandler = new ExecutionMarketChecks(missingHistoryIndex: 119);
        using var missingLatestClient = Client(missingLatestHandler);
        using var missingLatestResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(missingLatestClient, timeProvider: fixedClock).Read(
                ["BTC"],
                CancellationToken.None
            )
        );
        Check(
            missingLatestResult.RootElement.GetProperty("markets").GetArrayLength() == 0,
            "A contiguous historical window must not be accepted without the latest completed candle."
        );

        using var noDataHandler = new ExecutionMarketChecks(unavailableHistory: true);
        using var noDataClient = Client(noDataHandler);
        using var noDataResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(noDataClient, timeProvider: fixedClock).Read(
                ["BTC"],
                CancellationToken.None
            )
        );
        Check(
            noDataResult.RootElement.GetProperty("markets").GetArrayLength() == 0
                && noDataResult.RootElement.GetProperty("errors")[0].GetProperty("error").GetString()
                    == "Fewer than 51 consecutive recent completed candles could be verified.",
            "Provider no_data must remain a history-specific failure without invented prices."
        );

        using var quotesOnlyHandler = new ExecutionMarketChecks(missingHistoryIndex: 90);
        using var quotesOnlyClient = Client(quotesOnlyHandler);
        await new BitkubExecutionMarketReader(quotesOnlyClient).Read(
            ["BTC"],
            CancellationToken.None,
            includeHistory: false
        );
        Check(
            !quotesOnlyHandler.requests.Any(uri => uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal)),
            "includeHistory=false must make zero history requests."
        );

        using var staleHandler = new ExecutionMarketChecks();
        using var staleClient = Client(staleHandler);
        var staleSnapshot = Snapshot(fixedNow - TimeSpan.FromMinutes(6), suffixBars);
        using var staleResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(
                staleClient,
                new ResearchIntradayReader(staleSnapshot),
                fixedClock
            ).Read(["BTC"], CancellationToken.None)
        );
        Check(
            staleHandler.requests.Any(uri => uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal))
                && staleResult.RootElement.GetProperty("markets")[0].GetProperty("bars").GetArrayLength() == 120,
            "A stale latest feed must be rejected and replaced with fresh history."
        );

        using var oversizedHandler = new ExecutionMarketChecks(metadata: new string(' ', 1_048_577));
        using var oversizedClient = Client(oversizedHandler);
        using var oversizedResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(oversizedClient).Read(["BTC"], CancellationToken.None)
        );
        Check(
            oversizedResult.RootElement.GetProperty("markets").GetArrayLength() == 0
                && oversizedResult.RootElement.GetProperty("errors").GetArrayLength() == 1,
            "Oversized public data must fail closed."
        );

        var now = DateTimeOffset.UtcNow;
        var to = now.ToUnixTimeSeconds() / 300 * 300;
        var feedBars = Enumerable
            .Range(0, 120)
            .Select(index => new MarketCandle(to - (120 - index) * 300, 20, 21, 19, 20, 1))
            .ToArray();
        var feed = new ResearchIntradayReader(
            new IntradaySnapshot(true, now, true, [], [new IntradaySeries("BTC_THB", feedBars, now, null)], [], null)
        );
        using var feedHandler = new ExecutionMarketChecks();
        using var feedClient = Client(feedHandler);
        using var feedResult = JsonDocument.Parse(
            await new BitkubExecutionMarketReader(feedClient, feed).Read(["BTC"], CancellationToken.None)
        );
        Check(
            !feedHandler.requests.Any(uri => uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal))
                && feedResult.RootElement.GetProperty("markets")[0].GetProperty("bars").GetArrayLength() == 120,
            "Fresh full retained feed must avoid redundant history without truncating engine bars."
        );
        Console.WriteLine("Execution market checks passed.");
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing fake request URI.");
        Check(request.Method == HttpMethod.Get, "Execution evidence must only make public GET requests.");
        this.requests.Enqueue(uri);
        string payload;
        if (uri.AbsolutePath.EndsWith("symbols", StringComparison.Ordinal))
        {
            payload =
                this.metadata
                ?? JsonSerializer.Serialize(
                    new
                    {
                        error = 0,
                        result = ExecutionMarketChecks.fixtureSymbols.Select(symbol => new
                        {
                            base_asset = symbol,
                            quote_asset = "THB",
                            status = "active",
                            market_segment = "SPOT",
                            source = "exchange",
                            min_quote_size = 10,
                            price_step = "0.000000000000001",
                            price_scale = 15,
                            base_asset_scale = 15,
                            quote_asset_scale = 2,
                            freeze_buy = false,
                            freeze_sell = symbol == "NEAR",
                        }),
                    }
                );
        }
        else if (uri.AbsolutePath.EndsWith("depth", StringComparison.Ordinal))
        {
            if (uri.Query.Contains("NEAR", StringComparison.Ordinal) && this.timeoutDepth)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            payload =
                uri.Query.Contains("NEAR", StringComparison.Ordinal) && this.invalidDepth
                    ? "{\"error\":0,\"result\":{\"asks\":[],\"bids\":[]}}"
                : uri.Query.Contains("BTC", StringComparison.Ordinal)
                    ? "{\"error\":0,\"asks\":[[10.223456789012345,0.234567890123456]],\"bids\":[[10.123456789012345,0.123456789012345]]}"
                : "{\"error\":0,\"result\":{\"asks\":[[10.223456789012345,0.234567890123456]],\"bids\":[[10.123456789012345,0.123456789012345]]}}";
        }
        else if (uri.AbsolutePath.EndsWith("history", StringComparison.Ordinal))
        {
            if (this.unavailableHistory)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"s\":\"no_data\"}"),
                };
            }

            var to = long.Parse(
                uri.Query.Split('&').Single(part => part.StartsWith("to=", StringComparison.Ordinal))[3..],
                CultureInfo.InvariantCulture
            );
            var timestamps = Enumerable
                .Range(0, this.historyRows)
                .Select(index => to - (this.historyRows - 1 - index) * 300)
                .Where((timestamp, index) => this.missingHistoryIndex != index)
                .ToArray();
            var prices = Enumerable.Repeat(10.123456789012345m, timestamps.Length).ToArray();
            payload = JsonSerializer.Serialize(
                new
                {
                    s = "ok",
                    t = timestamps,
                    o = prices,
                    h = prices,
                    l = prices,
                    c = prices,
                    v = prices,
                }
            );
        }
        else
        {
            throw new InvalidOperationException("Unexpected endpoint in execution market fake.");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) };
    }

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://test.invalid") };

    private static IntradaySnapshot Snapshot(DateTimeOffset fetchedAt, IReadOnlyList<MarketCandle> candles) =>
        new(true, fetchedAt, true, [], [new IntradaySeries("BTC_THB", candles, fetchedAt, null)], [], null);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
