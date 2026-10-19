using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class MarketDataTradingHistoryReader(HttpClient client) : ITradingHistoryReader
{
    public async Task<IReadOnlyList<TradingBar>> Read(
        string symbol,
        int timeframeSeconds,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrEmpty(symbol)
            || symbol.Length > 30
            || symbol.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            || !TradingTimeframes.Supported(timeframeSeconds)
            || from >= until
            || until - from > TimeSpan.FromDays(31)
            || from.ToUnixTimeSeconds() < 0
            || from.ToUnixTimeSeconds() % timeframeSeconds != 0
            || until.ToUnixTimeSeconds() % timeframeSeconds != 0
        )
        {
            throw new ArgumentException("Historical requests require a bounded native candle window.");
        }

        var result = new SortedDictionary<long, TradingBar>();
        for (var start = from.ToUnixTimeSeconds(); start < until.ToUnixTimeSeconds(); )
        {
            var end = Math.Min(until.ToUnixTimeSeconds(), start + 999L * timeframeSeconds);
            var uri = FormattableString.Invariant(
                $"api/market/trading/history?symbol={Uri.EscapeDataString(symbol)}&timeframeSeconds={timeframeSeconds}&from={start}&to={end}"
            );
            using var response = await client.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(1_000_000, cancellationToken);
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;
                if (
                    root.GetProperty("symbol").GetString() != symbol
                    || root.GetProperty("timeframeSeconds").GetInt32() != timeframeSeconds
                    || root.GetProperty("source").GetString() != "bitkub-tradingview"
                    || root.GetProperty("from").GetInt64() != start
                    || root.GetProperty("to").GetInt64() != end
                    || root.GetProperty("error").ValueKind != JsonValueKind.Null
                )
                {
                    throw new InvalidDataException("Historical candle provenance is unavailable or inconsistent.");
                }

                var candles = root.GetProperty("candles");
                if (candles.ValueKind != JsonValueKind.Array || candles.GetArrayLength() > 1000)
                {
                    throw new InvalidDataException("Historical candle response exceeds its window.");
                }

                foreach (var candle in candles.EnumerateArray())
                {
                    var bar =
                        candle.Deserialize<TradingBar>(TradingJson.Options)
                        ?? throw new InvalidDataException("Historical candle is missing.");
                    if (
                        bar.Timestamp < start
                        || bar.Timestamp >= end
                        || bar.Timestamp % timeframeSeconds != 0
                        || bar.Open <= 0
                        || bar.Close <= 0
                        || bar.Low <= 0
                        || bar.Volume < 0
                        || bar.High < Math.Max(bar.Open, bar.Close)
                        || bar.Low > Math.Min(bar.Open, bar.Close)
                        || !result.TryAdd(bar.Timestamp, bar)
                    )
                    {
                        throw new InvalidDataException("Historical candles must be valid, completed and unique.");
                    }
                }
            }
            catch (Exception exception)
                when (exception
                        is JsonException
                            or KeyNotFoundException
                            or InvalidOperationException
                            or FormatException
                            or OverflowException
                )
            {
                throw new InvalidDataException("Historical candle envelope is invalid.", exception);
            }

            start = end;
        }

        return result.Values.ToArray();
    }
}
