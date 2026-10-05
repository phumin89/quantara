using System.Net;
using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Api;
using TradingCompany.Domain;
using Xunit;
namespace TradingCompany.Domain.Tests;
public class TradingTests
{
    private static readonly PortfolioSnapshot Portfolio = new(100_000m, 90_000m, 0m, 0m, 0m, 0);
    private static readonly RiskLimits Limits = new(10_000m, new(.25m), 2_000m, new(.10m), 5, false);
    [Theory] [InlineData(-.01)] [InlineData(1.01)] public void Percentage_rejects_out_of_range(decimal value) => Assert.Throws<ArgumentOutOfRangeException>(() => new Percentage(value));
    [Fact] public void Symbol_normalizes_input() => Assert.Equal("NVDA", new Symbol(" nvda ").Value);
    [Fact] public void Risk_rejects_order_above_notional() { var proposal = Proposal(101, 100m); var result = new RiskEngine().Evaluate(proposal, Portfolio, Limits); Assert.Contains(result.Violations, x => x.Code == "MAX_ORDER_NOTIONAL_EXCEEDED"); }
    [Fact]
    public void Risk_allows_cash_backed_top_up_when_position_limit_is_full()
    {
        var portfolio = new PortfolioSnapshot(100_000m, 20_000m, 80_000m, 0m, 0m, 5);
        var proposal = new TradeProposal(Guid.NewGuid(), new("NVDA"), OrderSide.Buy, 10m, 100m, new(.8m), Guid.Empty, OpensNewPosition: false);
        var limits = new RiskLimits(20_000m, new(.95m), decimal.MaxValue, new(.25m), 5, false);

        var result = new RiskEngine().Evaluate(proposal, portfolio, limits);

        Assert.True(result.Approved);
    }
    [Fact] public void Risk_kill_switch_always_rejects() { var result = new RiskEngine().Evaluate(Proposal(1, 100m), Portfolio, Limits with { KillSwitchEnabled = true }); Assert.False(result.Approved); }
    [Fact]
    public void Risk_allows_sell_to_reduce_exposure_during_buy_limit_breaches()
    {
        var sell = SellProposal(100, 100m);
        var stressed = new PortfolioSnapshot(80_000m, 0m, 80_000m, 20_000m, 20_000m, 4);
        var restrictive = new RiskLimits(1m, new(0m), 1m, new(0m), 0, false);
        Assert.True(new RiskEngine().Evaluate(sell, stressed, restrictive).Approved);
    }
    [Fact]
    public async Task Real_crypto_provider_reads_native_thb_bars_from_bitkub()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host switch
        {
            "api.bitkub.com" => BitkubRows(new(2024, 1, 1), 200),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        });
        var provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(handler)));

        var bars = await provider.GetAsync(new("BTC-THB"), new(2024, 1, 1), new(2024, 7, 18), default);

        Assert.Equal(200, bars.Count);
        Assert.Equal(3m, bars[0].Close);
        Assert.Equal(7, bars[0].Volume);
    }
    [Fact]
    public async Task Real_crypto_provider_keeps_legacy_usdt_symbols_readable_during_migration()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "api.bitkub.com"
            ? BitkubRows(new(2024, 1, 1), 200)
            : throw new InvalidOperationException(request.RequestUri.ToString()));
        var provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(handler)));

        var bars = await provider.GetAsync(new("BTC-USDT"), new(2024, 1, 1), new(2024, 7, 18), default);

        Assert.Equal(200, bars.Count);
        Assert.All(bars, bar => Assert.Equal("BTC-USDT", bar.Symbol.Value));
    }
    [Fact]
    public async Task Real_crypto_provider_translates_malformed_bitkub_schema_to_invalid_data()
    {
        var provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(new StubHttpHandler(_ => "{\"s\":\"ok\",\"t\":[]}"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetAsync(new("BTC-THB"), new(2024, 1, 1), new(2024, 7, 18), default));
    }
    [Fact]
    public async Task Real_equity_provider_rejects_ohlc_bar_outside_high_low_envelope()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "query1.finance.yahoo.com"
            ? YahooRows(new(2024, 1, 1), 200, malformedIndex: 100)
            : throw new InvalidOperationException(request.RequestUri.ToString()));
        var provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(handler)), new FixedUsdThbRateProvider());

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetAsync(new("NVDA"), new(2024, 1, 1), new(2024, 7, 18), default));
    }
    [Fact]
    public async Task Real_bk_provider_keeps_yahoo_prices_native_and_skips_fx()
    {
        var rates = new SpyUsdThbRateProvider();
        var provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(new StubHttpHandler(_ => YahooRows(new(2024, 1, 1), 200, -1)))), rates);
        var bars = await provider.GetAsync(new("PTT.BK"), new(2024, 1, 1), new(2024, 7, 18), default);
        Assert.Equal(3m, bars[0].Close);
        Assert.Equal(0, rates.HistoricalCalls);
    }
    [Fact]
    public async Task Real_us_provider_uses_date_aligned_prior_fx_only()
    {
        var rates = new SpyUsdThbRateProvider([
            new(new(2024, 1, 1), 2m), new(new(2024, 1, 2), 3m)]);
        var provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(new StubHttpHandler(_ => YahooRows(new(2024, 1, 1), 200, -1)))), rates);
        var bars = await provider.GetAsync(new("NVDA"), new(2024, 1, 1), new(2024, 7, 18), default);
        Assert.Equal(6m, bars[0].Close);
        Assert.Equal(9m, bars[1].Close);
        Assert.Equal(1, rates.HistoricalCalls);
        Assert.Equal(0, rates.LiveCalls);
    }
    [Fact]
    public async Task Snapshot_evidence_retains_source_prices_and_the_fx_series_used()
    {
        var rates = new SpyUsdThbRateProvider([new(new(2024, 1, 1), 2m), new(new(2024, 1, 2), 3m)]);
        object provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(
            new StubHttpHandler(_ => YahooRows(new(2024, 1, 1), 200, -1)))), rates);
        var evidenceProvider = Assert.IsAssignableFrom<IHistoricalMarketEvidenceProvider>(provider);
        var evidence = await evidenceProvider.GetEvidenceAsync(new("NVDA"), new(2024, 1, 1), new(2024, 7, 18), default);
        Assert.Equal(3m, evidence.SourceBars[0].Close);
        Assert.Equal(6m, evidence.Bars[0].Close);
        Assert.Equal(9m, evidence.Bars[1].Close);
        Assert.Equal("USD", evidence.SourceCurrency);
        Assert.Equal([new SnapshotFxRate(new(2024, 1, 1), 2m), new SnapshotFxRate(new(2024, 1, 2), 3m)], evidence.FxRates);
    }
    [Fact]
    public async Task Real_yahoo_provider_applies_adjusted_close_factor_to_ohlc()
    {
        var payload = YahooRowsWithAdjustedClose(new(2024, 1, 1), 200, adjustedClose: 1.5m);
        var provider = new RealHistoricalMarketDataProvider(
            new FakeHttpClientFactory(new HttpClient(new StubHttpHandler(_ => payload))),
            new FixedUsdThbRateProvider());

        var bars = await provider.GetAsync(new("VOO"), new(2024, 1, 1), new(2024, 7, 18), default);

        Assert.Equal(.5m, bars[0].Open);
        Assert.Equal(2m, bars[0].High);
        Assert.Equal(.25m, bars[0].Low);
        Assert.Equal(1.5m, bars[0].Close);
    }
    [Fact]
    public async Task Real_us_provider_fails_when_no_prior_fx_exists()
    {
        var rates = new SpyUsdThbRateProvider([new(new(2024, 1, 2), 2m)]);
        var provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(new StubHttpHandler(_ => YahooRows(new(2024, 1, 1), 200, -1)))), rates);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetAsync(new("NVDA"), new(2024, 1, 1), new(2024, 7, 18), default));
    }
    [Theory]
    [InlineData("{\"chart\":{\"result\":[]}}")]
    [InlineData("{\"chart\":{\"result\":[{\"timestamp\":[1],\"indicators\":{\"quote\":[{\"open\":[],\"high\":[1],\"low\":[1],\"close\":[1],\"volume\":[1]}]}}]}}")]
    [InlineData("{\"chart\":{\"result\":[{\"timestamp\":[1],\"indicators\":{\"quote\":[{\"open\":[null],\"high\":[1],\"low\":[1],\"close\":[1],\"volume\":[1]}]}}]}}")]
    public async Task Real_yahoo_provider_translates_malformed_schema_to_invalid_data(string payload)
    {
        var provider = new RealHistoricalMarketDataProvider(new FakeHttpClientFactory(new HttpClient(new StubHttpHandler(_ => payload))), new SpyUsdThbRateProvider());
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetAsync(new("NVDA"), new(2024, 1, 1), new(2024, 7, 18), default));
    }
    [Fact]
    public async Task Real_yahoo_provider_skips_an_incomplete_bar_when_enough_valid_history_remains()
    {
        var payload = YahooRowsWithNull(new(2024, 1, 1), 201, 100);
        var provider = new RealHistoricalMarketDataProvider(
            new FakeHttpClientFactory(new HttpClient(new StubHttpHandler(_ => payload))),
            new FixedUsdThbRateProvider());

        var bars = await provider.GetAsync(new("NVDA"), new(2024, 1, 1), new(2024, 7, 19), default);

        Assert.Equal(200, bars.Count);
        Assert.DoesNotContain(bars, bar => bar.Date == new DateOnly(2024, 4, 10));
    }
    [Fact]
    public async Task Frankfurter_historical_series_is_parsed_and_same_range_is_cached()
    {
        var calls = 0;
        var handler = new StubHttpHandler(_ => { Interlocked.Increment(ref calls); return "[{\"date\":\"2024-01-01\",\"base\":\"USD\",\"quote\":\"THB\",\"rate\":34.1}]"; });
        var provider = new FrankfurterUsdThbRateProvider(new FakeHttpClientFactory(new HttpClient(handler)));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => provider.GetHistoricalAsync(new(2024, 1, 1), new(2024, 1, 2), default)));
        Assert.Equal(1, calls);
        Assert.Equal(34.1m, results[0].Single().Rate);
    }
    [Fact]
    public void Deterministic_forecast_distinguishes_rising_and_falling_markets()
    {
        var symbol = new Symbol("NVDA");
        var rising = DeterministicForecaster.Evaluate(RisingBars(symbol, 220), symbol);
        var falling = DeterministicForecaster.Evaluate(FallingBars(symbol, 220), symbol);
        Assert.Equal(ForecastDirection.Bullish, rising.Direction);
        Assert.True(rising.Score >= 70m);
        Assert.Equal(ForecastDirection.Bearish, falling.Direction);
        Assert.True(falling.Score <= 35m);
    }
    [Fact]
    public void Crypto_quantity_supports_fractional_units_while_equities_remain_whole()
    {
        Assert.Equal(.007782m, TradeQuantity.FloorFor(new("BTC-THB"), 20_000m / 2_569_969.519m));
        Assert.Equal(2m, TradeQuantity.FloorFor(new("NVDA"), 2.9m));
    }
    private static TradeProposal Proposal(int quantity, decimal price) => new(Guid.NewGuid(), new("NVDA"), OrderSide.Buy, quantity, price, new(.72m), Guid.NewGuid());
    private static TradeProposal SellProposal(int quantity, decimal price) => new(Guid.NewGuid(), new("NVDA"), OrderSide.Sell, quantity, price, new(.9m), Guid.NewGuid());
    private static List<HistoricalBar> RisingBars(Symbol symbol, int count)
    {
        var start = new DateOnly(2025, 1, 1);
        return Enumerable.Range(0, count)
            .Select(index => new HistoricalBar(start.AddDays(index), symbol, 100m + index, 101m + index, 99m + index, 100m + index, 1_000))
            .ToList();
    }
    private static List<HistoricalBar> FallingBars(Symbol symbol, int count)
    {
        var start = new DateOnly(2025, 1, 1);
        return Enumerable.Range(0, count)
            .Select(index => { var price = 400m - index; return new HistoricalBar(start.AddDays(index), symbol, price, price + 1m, price - 1m, price, 1_000); })
            .ToList();
    }
    private static string BitkubRows(DateOnly start, int count) => JsonSerializer.Serialize(new
    {
        s = "ok",
        t = Enumerable.Range(0, count).Select(index => new DateTimeOffset(start.AddDays(index).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds()).ToArray(),
        o = Enumerable.Repeat(1m, count).ToArray(),
        h = Enumerable.Repeat(4m, count).ToArray(),
        l = Enumerable.Repeat(.5m, count).ToArray(),
        c = Enumerable.Repeat(3m, count).ToArray(),
        v = Enumerable.Repeat(7m, count).ToArray()
    });
    private static string YahooRows(DateOnly start, int count, int malformedIndex)
    {
        var timestamps = Enumerable.Range(0, count).Select(index => new DateTimeOffset(start.AddDays(index).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds()).ToArray();
        var opens = Enumerable.Repeat(1m, count).ToArray();
        var highs = Enumerable.Repeat(4m, count).ToArray();
        var lows = Enumerable.Repeat(.5m, count).ToArray();
        var closes = Enumerable.Repeat(3m, count).ToArray();
        if (malformedIndex >= 0) closes[malformedIndex] = 5m;
        return JsonSerializer.Serialize(new { chart = new { result = new[] { new { timestamp = timestamps, indicators = new { quote = new[] { new { open = opens, high = highs, low = lows, close = closes, volume = Enumerable.Repeat(7L, count).ToArray() } } } } }, error = (object?)null } });
    }
    private static string YahooRowsWithNull(DateOnly start, int count, int nullIndex)
    {
        var timestamps = Enumerable.Range(0, count).Select(index => new DateTimeOffset(start.AddDays(index).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds()).ToArray();
        var opens = Enumerable.Repeat<decimal?>(1m, count).ToArray();
        var highs = Enumerable.Repeat<decimal?>(4m, count).ToArray();
        var lows = Enumerable.Repeat<decimal?>(.5m, count).ToArray();
        var closes = Enumerable.Repeat<decimal?>(3m, count).ToArray();
        var volumes = Enumerable.Repeat<long?>(7L, count).ToArray();
        closes[nullIndex] = null;
        return JsonSerializer.Serialize(new { chart = new { result = new[] { new { timestamp = timestamps, indicators = new { quote = new[] { new { open = opens, high = highs, low = lows, close = closes, volume = volumes } } } } }, error = (object?)null } });
    }
    private static string YahooRowsWithAdjustedClose(DateOnly start, int count, decimal adjustedClose)
    {
        var timestamps = Enumerable.Range(0, count).Select(index => new DateTimeOffset(start.AddDays(index).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds()).ToArray();
        return JsonSerializer.Serialize(new
        {
            chart = new
            {
                result = new[]
                {
                    new
                    {
                        timestamp = timestamps,
                        indicators = new
                        {
                            quote = new[] { new { open = Enumerable.Repeat(1m, count), high = Enumerable.Repeat(4m, count), low = Enumerable.Repeat(.5m, count), close = Enumerable.Repeat(3m, count), volume = Enumerable.Repeat(7L, count) } },
                            adjclose = new[] { new { adjclose = Enumerable.Repeat(adjustedClose, count) } }
                        }
                    }
                },
                error = (object?)null
            }
        });
    }
    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class FixedUsdThbRateProvider : IUsdThbRateProvider
    {
        public Task<decimal> GetAsync(CancellationToken cancellationToken) => Task.FromResult(1m);
        public Task<IReadOnlyList<HistoricalUsdThbRate>> GetHistoricalAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<HistoricalUsdThbRate>>([new(start, 1m)]);
    }
    private sealed class SpyUsdThbRateProvider(IReadOnlyList<HistoricalUsdThbRate>? historical = null) : IUsdThbRateProvider
    {
        public int LiveCalls { get; private set; }
        public int HistoricalCalls { get; private set; }
        public Task<decimal> GetAsync(CancellationToken cancellationToken) { LiveCalls++; return Task.FromResult(99m); }
        public Task<IReadOnlyList<HistoricalUsdThbRate>> GetHistoricalAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
        { HistoricalCalls++; return Task.FromResult(historical ?? (IReadOnlyList<HistoricalUsdThbRate>)[]); }
    }
    private sealed class StubHttpHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
    }
}
