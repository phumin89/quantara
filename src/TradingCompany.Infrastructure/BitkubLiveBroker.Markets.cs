using System.Text.Json;
using TradingCompany.Domain;

namespace TradingCompany.Infrastructure;

public sealed partial class BitkubLiveBroker
{
    public async Task<IReadOnlyList<BrokerSymbolRules>> GetAvailableMarketsAsync(CancellationToken token = default)
    {
        var observedAt = clock.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bitkub.com/api/v3/market/symbols");
        using var document = await Read(request, token);
        try
        {
            var rows = Required(document.RootElement, "result");
            if (Required(document.RootElement, "error").GetInt32() != 0 || rows.ValueKind != JsonValueKind.Array ||
                rows.GetArrayLength() > LiveMarketSymbol.MaximumPayloadMarkets) throw Unavailable();
            var markets = new List<BrokerSymbolRules>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in rows.EnumerateArray())
            {
                if (Required(item, "quote_asset").GetString() != "THB" || Required(item, "source").GetString() != "exchange" ||
                    Required(item, "market_segment").GetString() != "SPOT" || Required(item, "status").GetString() != "active" ||
                    Required(item, "freeze_buy").GetBoolean() || Required(item, "freeze_sell").GetBoolean() || Required(item, "freeze_cancel").GetBoolean()) continue;
                var symbol = Required(item, "symbol").GetString()?.Replace('_', '-') ?? throw Unavailable();
                if (!LiveMarketSymbol.IsValid(symbol) || !seen.Add(symbol)) throw Unavailable();
                var market = ParseMarket(item, symbol, observedAt);
                if (!BitkubProfileVerifier.IsReceiptMarket(market, observedAt)) throw Unavailable();
                markets.Add(market);
            }
            if (markets.Count == 0) throw Unavailable();
            return markets.OrderBy(m => m.Symbol, StringComparer.Ordinal).ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    private static BrokerSymbolRules ParseMarket(JsonElement item, string symbol, DateTimeOffset observedAt)
    {
        if (Required(item, "base_asset").GetString() != symbol[..^4] || Required(item, "quote_asset").GetString() != "THB") throw Unavailable();
        var result = new BrokerSymbolRules(symbol, observedAt, Scale(item, "base_asset_scale"), Scale(item, "quote_asset_scale"),
            OptionalScale(item, "base_quantity_scale"), OptionalScale(item, "quote_quantity_scale"), Scale(item, "price_scale"),
            Amount(item, "price_step"), Scale(item, "quantity_scale"), Amount(item, "quantity_step"), NumericAmount(item, "min_quote_size"),
            Required(item, "freeze_buy").GetBoolean(), Required(item, "freeze_sell").GetBoolean(), Required(item, "freeze_cancel").GetBoolean(),
            Identifier(item, "status"), Identifier(item, "source"), Identifier(item, "market_segment"));
        if (result.PriceStep <= 0 || result.QuantityStep <= 0 || result.MinimumQuoteSize <= 0) throw Unavailable();
        return result;
    }

    public async Task<IReadOnlyList<LiveQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken token = default)
    {
        ValidateMarketList(symbols);
        var observedAt = clock.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bitkub.com/api/v3/market/ticker");
        using var document = await Read(request, token);
        try
        {
            var rows = document.RootElement;
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > LiveMarketSymbol.MaximumPayloadMarkets) throw Unavailable();
            var wanted = symbols.ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var quotes = new List<LiveQuote>();
            foreach (var item in rows.EnumerateArray())
            {
                var symbol = Required(item, "symbol").GetString()?.ToUpperInvariant().Replace('_', '-') ?? throw Unavailable();
                if (!wanted.Contains(symbol)) continue;
                if (!seen.Add(symbol)) throw Unavailable();
                var bid = Amount(item, "highest_bid"); var ask = Amount(item, "lowest_ask");
                // Missing/zero books are unavailable markets, never usable synthetic quotes.
                if (bid == 0 || ask == 0) continue;
                if (ask < bid) throw Unavailable();
                quotes.Add(new(symbol, bid, ask, observedAt));
            }
            return quotes.OrderBy(q => q.Symbol, StringComparer.Ordinal).ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    public async Task<IReadOnlyList<BrokerOpenOrderEvidence>> GetOpenOrdersForMarketsAsync(IReadOnlyList<string> symbols, CancellationToken token = default)
        => (await ReadMarketBatchesAsync(symbols, GetOpenOrdersAsync, token)).SelectMany(x => x).ToArray();

    internal static async Task<IReadOnlyList<T>> ReadMarketBatchesAsync<T>(IReadOnlyList<string> symbols,
        Func<string, CancellationToken, Task<T>> read, CancellationToken token)
    {
        ValidateMarketList(symbols);
        var results = new List<T>();
        // At most eight concurrent reads. Normal one-page paired open/history scans
        // schedule <=80 reads/sec per endpoint and <=160 fresh server times/sec.
        // Two full bounded catalogs fit the 30-second freshness window before network
        // latency; slow/large-history observations still fail closed. No retries.
        foreach (var batch in symbols.Chunk(8))
        {
            if (results.Count > 0) await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            results.AddRange(await Task.WhenAll(batch.Select(symbol => read(symbol, token))));
        }
        return results;
    }

    private static void ValidateMarketList(IReadOnlyList<string>? symbols)
    {
        if (symbols is null || symbols.Count is 0 or > LiveMarketSymbol.MaximumPayloadMarkets ||
            symbols.Any(s => !LiveMarketSymbol.IsValid(s)) || symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Count) throw Unavailable();
    }
}
