using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.Api;

/// Public, read-only market data. Any upstream failure is surfaced; synthetic data is never used.
public sealed class RealHistoricalMarketDataProvider(IHttpClientFactory clients, IUsdThbRateProvider? rates = null) : IHistoricalMarketEvidenceProvider
{
    private readonly IUsdThbRateProvider rates = rates ?? new FrankfurterUsdThbRateProvider(clients);
    public async Task<IReadOnlyList<HistoricalBar>> GetAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
        => (await GetEvidenceAsync(symbol, start, end, cancellationToken)).Bars;

    public async Task<HistoricalMarketEvidence> GetEvidenceAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        if (end < start) throw new ArgumentException("End date must not precede start date.");
        if (TryGetBitkubPair(symbol, out var pair))
        {
            var native = await GetBitkubAsync(symbol, pair, start, end, cancellationToken);
            return new(native, native, [], "bitkub-daily", "THB");
        }
        if (symbol.Value.EndsWith("-THB", StringComparison.Ordinal) || symbol.Value.EndsWith("-USDT", StringComparison.Ordinal))
            throw new InvalidOperationException($"No Bitkub mapping exists for {symbol.Value}.");

        var bars = await GetYahooAsync(symbol, start, end, cancellationToken);
        if (symbol.Value.EndsWith(".BK", StringComparison.Ordinal)) return new(bars, bars, [], "yahoo-adjusted-when-supplied", "THB");
        var fx = await rates.GetHistoricalAsync(start.AddDays(-7), end, cancellationToken);
        var converted = bars.Select(x =>
        {
            var rate = fx.Where(r => r.Date <= x.Date).OrderByDescending(r => r.Date).FirstOrDefault();
            if (rate is null) throw new InvalidDataException($"No historical USD/THB rate available for {x.Date:yyyy-MM-dd}.");
            return x with { Open = x.Open * rate.Rate, High = x.High * rate.Rate, Low = x.Low * rate.Rate, Close = x.Close * rate.Rate };
        }).ToArray();
        return new(converted, bars, fx.Select(r => new SnapshotFxRate(r.Date, r.Rate)).ToArray(),
            "yahoo-adjusted-when-supplied+frankfurter-historical-fx", "USD");
    }

    private static bool TryGetBitkubPair(Symbol symbol, out string pair)
    {
        pair = symbol.Value switch
        {
            "BTC-THB" or "BTC-USDT" => "BTC_THB",
            "ETH-THB" or "ETH-USDT" => "ETH_THB",
            "SOL-THB" or "SOL-USDT" => "SOL_THB",
            _ => string.Empty
        };
        return pair.Length > 0;
    }

    private async Task<IReadOnlyList<HistoricalBar>> GetYahooAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var from = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var to = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var ticker = Uri.EscapeDataString(symbol.Value);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://query1.finance.yahoo.com/v8/finance/chart/{ticker}?period1={from}&period2={to}&interval=1d");
        request.Headers.UserAgent.ParseAdd("QuantaraPaper/2.0");
        using var response = await clients.CreateClient("market-data").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        try
        {
            var results = json.RootElement.GetProperty("chart").GetProperty("result");
            if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0 || results[0].ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Yahoo Finance returned no chart result.");
            var result = results[0];
            var timestamps = result.GetProperty("timestamp").EnumerateArray().Select(x => x.GetInt64()).ToArray();
            var indicators = result.GetProperty("indicators");
            var quoteResults = indicators.GetProperty("quote");
            if (quoteResults.ValueKind != JsonValueKind.Array || quoteResults.GetArrayLength() == 0) throw new InvalidDataException("Yahoo Finance returned no quote data.");
            var quote = quoteResults[0];
            var opens = quote.GetProperty("open").EnumerateArray().ToArray(); var highs = quote.GetProperty("high").EnumerateArray().ToArray(); var lows = quote.GetProperty("low").EnumerateArray().ToArray(); var closes = quote.GetProperty("close").EnumerateArray().ToArray(); var volumes = quote.GetProperty("volume").EnumerateArray().ToArray();
            JsonElement[]? adjustedCloses = null;
            if (indicators.TryGetProperty("adjclose", out var adjustedResults) && adjustedResults.ValueKind == JsonValueKind.Array && adjustedResults.GetArrayLength() > 0)
            {
                var adjusted = adjustedResults[0].GetProperty("adjclose");
                if (adjusted.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Yahoo Finance returned malformed adjusted close data.");
                adjustedCloses = adjusted.EnumerateArray().ToArray();
            }
            if (timestamps.Length == 0 || new[] { opens.Length, highs.Length, lows.Length, closes.Length, volumes.Length }.Any(length => length != timestamps.Length))
                throw new InvalidDataException("Yahoo Finance returned mismatched history arrays.");
            if (adjustedCloses is not null && adjustedCloses.Length != timestamps.Length)
                throw new InvalidDataException("Yahoo Finance returned mismatched adjusted close data.");
            var usableEnd = CompletedEnd(end);
            var bars = new List<HistoricalBar>(timestamps.Length);
            for (var i = 0; i < timestamps.Length; i++)
            {
                if (opens[i].ValueKind != JsonValueKind.Number || highs[i].ValueKind != JsonValueKind.Number ||
                    lows[i].ValueKind != JsonValueKind.Number || closes[i].ValueKind != JsonValueKind.Number ||
                    volumes[i].ValueKind != JsonValueKind.Number ||
                    !opens[i].TryGetDecimal(out var open) || !highs[i].TryGetDecimal(out var high) ||
                    !lows[i].TryGetDecimal(out var low) || !closes[i].TryGetDecimal(out var close) ||
                    !volumes[i].TryGetDecimal(out var volume)) continue;
                var adjustment = 1m;
                if (adjustedCloses is not null)
                {
                    if (adjustedCloses[i].ValueKind != JsonValueKind.Number || !adjustedCloses[i].TryGetDecimal(out var adjustedClose) || adjustedClose <= 0m || close <= 0m) continue;
                    adjustment = adjustedClose / close;
                }
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).UtcDateTime);
                if (date <= usableEnd)
                    bars.Add(new HistoricalBar(date, symbol, open * adjustment, high * adjustment, low * adjustment, close * adjustment, decimal.ToInt64(decimal.Truncate(volume))));
            }
            Validate(bars, start, end, "Yahoo Finance");
            return bars;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or FormatException or OverflowException)
        { throw new InvalidDataException("Yahoo Finance returned malformed history data.", ex); }
    }

    private async Task<IReadOnlyList<HistoricalBar>> GetBitkubAsync(Symbol symbol, string pair, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var from = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var to = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds() - 1;
        var url = $"https://api.bitkub.com/tradingview/history?symbol={pair}&resolution=1D&from={from}&to={to}";
        using var response = await clients.CreateClient("market-data").GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        try
        {
            var root = json.RootElement;
            if (!root.TryGetProperty("s", out var status) || status.GetString() != "ok") throw new InvalidDataException("Bitkub returned an unsuccessful history response.");
            var timestamps = root.GetProperty("t").EnumerateArray().ToArray();
            var opens = root.GetProperty("o").EnumerateArray().ToArray(); var highs = root.GetProperty("h").EnumerateArray().ToArray(); var lows = root.GetProperty("l").EnumerateArray().ToArray(); var closes = root.GetProperty("c").EnumerateArray().ToArray(); var volumes = root.GetProperty("v").EnumerateArray().ToArray();
            if (new[] { opens.Length, highs.Length, lows.Length, closes.Length, volumes.Length }.Any(length => length != timestamps.Length)) throw new InvalidDataException("Bitkub returned mismatched history arrays.");
            var usableEnd = CompletedEnd(end);
            var bars = Enumerable.Range(0, timestamps.Length).Select(index => new HistoricalBar(
                DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[index].GetInt64()).UtcDateTime), symbol,
                opens[index].GetDecimal(), highs[index].GetDecimal(), lows[index].GetDecimal(), closes[index].GetDecimal(), decimal.ToInt64(decimal.Truncate(volumes[index].GetDecimal())))).Where(x => x.Date <= usableEnd).ToArray();
            Validate(bars, start, end, "Bitkub");
            return bars;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or FormatException or OverflowException)
        { throw new InvalidDataException("Bitkub returned malformed history data.", ex); }
    }

    private static void Validate(IReadOnlyList<HistoricalBar> bars, DateOnly start, DateOnly end, string source)
    {
        var usableEnd = CompletedEnd(end);
        if (bars.Count < 200 || bars.Any(x => x.Date < start || x.Date > usableEnd || x.Open <= 0 || x.High <= 0 || x.Low <= 0 || x.Close <= 0 || x.High < x.Low || x.High < Math.Max(x.Open, x.Close) || x.Low > Math.Min(x.Open, x.Close)) || bars.Zip(bars.Skip(1)).Any(x => x.First.Date >= x.Second.Date))
            throw new InvalidDataException($"{source} returned empty, unordered, or invalid OHLC data.");
    }
    private static DateOnly CompletedEnd(DateOnly end) => end >= DateOnly.FromDateTime(DateTime.UtcNow) ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1) : end;
}
