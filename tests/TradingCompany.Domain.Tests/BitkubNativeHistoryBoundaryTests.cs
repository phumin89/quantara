using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using TradingCompany.Api;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubNativeHistoryBoundaryTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);
    private static readonly DateOnly End = Start.AddDays(399);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Leading_prior_day_bars_are_excluded_from_the_native_400_day_window(int leading)
    {
        var wire = new PublicWire(Payload(leading, 400, trailing: 2).ToJsonString());
        var rates = new NoFxRates();
        using var client = new HttpClient(wire);
        var provider = new RealHistoricalMarketDataProvider(new Factory(client), rates);
        var evidence = await provider.GetEvidenceAsync(new("XRP-THB"), Start, End, default);

        Assert.Equal(400, evidence.Bars.Count);
        Assert.Equal(Start, evidence.Bars[0].Date);
        Assert.Equal(End, evidence.Bars[^1].Date);
        Assert.Equal(Enumerable.Range(0, 400).Select(i => Start.AddDays(i)), evidence.Bars.Select(b => b.Date));
        Assert.All(evidence.Bars, b => Assert.Equal("XRP-THB", b.Symbol.Value));
        Assert.Equal(2.12345m, evidence.Bars[0].Open);
        Assert.Equal(2.12347m, evidence.Bars[0].Close);
        Assert.Equal(2.12744m, evidence.Bars[^1].Open);
        Assert.Equal(10, evidence.Bars[0].Volume);
        Assert.Equal(evidence.Bars, evidence.SourceBars);
        Assert.Equal("bitkub-daily", evidence.Source);
        Assert.Equal("THB", evidence.SourceCurrency);
        Assert.Empty(evidence.FxRates);
        Assert.Equal(0, rates.Calls);
        Assert.Equal(1, wire.Calls);
    }

    [Theory]
    [InlineData("invalid-high")]
    [InlineData("zero-close")]
    [InlineData("duplicate")]
    [InlineData("conflicting-duplicate")]
    public async Task Filtering_prior_day_bars_does_not_hide_invalid_or_duplicate_in_window_data(string defect)
    {
        const int leading = 2;
        var payload = Payload(leading, 400, trailing: 1);
        var index = leading + 100;
        if (defect == "invalid-high") payload["h"]![index] = payload["l"]![index]!.DeepClone();
        else if (defect == "zero-close") payload["c"]![index] = 0m;
        else
        {
            foreach (var key in new[] { "t", "o", "h", "l", "c", "v" })
                payload[key]!.AsArray().Insert(index + 1, payload[key]![index]!.DeepClone());
            if (defect == "conflicting-duplicate")
                payload["c"]![index + 1] = payload["c"]![index]!.GetValue<decimal>() + .00001m;
        }
        var wire = new PublicWire(payload.ToJsonString());
        using var client = new HttpClient(wire);
        var provider = new RealHistoricalMarketDataProvider(new Factory(client), new NoFxRates());
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetEvidenceAsync(new("XRP-THB"), Start, End, default));
        Assert.Equal(1, wire.Calls);
    }

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    public async Task Minimum_200_bar_guard_counts_only_bars_inside_the_requested_window(int inside, bool accepted)
    {
        var wire = new PublicWire(Payload(leading: 2, inside, trailing: 2).ToJsonString());
        using var client = new HttpClient(wire);
        var provider = new RealHistoricalMarketDataProvider(new Factory(client), new NoFxRates());
        if (accepted)
        {
            var evidence = await provider.GetEvidenceAsync(new("XRP-THB"), Start, End, default);
            Assert.Equal(200, evidence.Bars.Count);
            Assert.Equal(Start, evidence.Bars[0].Date);
            Assert.Equal(Start.AddDays(199), evidence.Bars[^1].Date);
        }
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetEvidenceAsync(new("XRP-THB"), Start, End, default));
        Assert.Equal(1, wire.Calls);
    }

    private static JsonObject Payload(int leading, int inside, int trailing)
    {
        var dates = Enumerable.Range(-leading, leading + inside).Select(i => Start.AddDays(i))
            .Concat(Enumerable.Range(1, trailing).Select(i => End.AddDays(i))).ToArray();
        var opens = dates.Select(d => 2.12345m + (d.DayNumber - Start.DayNumber) * .00001m).ToArray();
        return JsonSerializer.SerializeToNode(new
        {
            s = "ok", t = dates.Select(Timestamp).ToArray(), o = opens,
            h = opens.Select(p => p + .00010m).ToArray(), l = opens.Select(p => p - .00010m).ToArray(),
            c = opens.Select(p => p + .00002m).ToArray(), v = dates.Select(_ => 10.25m).ToArray()
        })!.AsObject();
    }
    private static long Timestamp(DateOnly date)
        => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Assert.Equal("market-data", name); return client; }
    }
    private sealed class NoFxRates : IUsdThbRateProvider
    {
        public int Calls { get; private set; }
        public Task<decimal> GetAsync(CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Native THB history must not request FX."); }
        public Task<IReadOnlyList<HistoricalUsdThbRate>> GetHistoricalAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Native THB history must not request historical FX."); }
    }
    private sealed class PublicWire(string payload) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://api.bitkub.com/tradingview/history?symbol=XRP_THB&resolution=1D&from={Timestamp(Start)}&to={Timestamp(End.AddDays(1)) - 1}",
                request.RequestUri!.AbsoluteUri);
            Assert.DoesNotContain(request.Headers, h => h.Key.StartsWith("X-BTK-", StringComparison.OrdinalIgnoreCase));
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }
}
