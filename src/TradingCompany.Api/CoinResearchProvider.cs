using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using TradingCompany.AgentRuntime;
using TradingCompany.Application;
using TradingCompany.Infrastructure;

namespace TradingCompany.Api;

public sealed class CoinResearchProvider(IHttpClientFactory clients, CoinSourceReader sources,
    CoinCredibilityResearcher researcher, TimeProvider clock, IRealtimeEventPublisher? events = null) : ICoinResearchProvider
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CoinResearchBrief? cached;
    private string? scope;
    private DateTimeOffset nextRead;

    public async Task<CoinResearchBrief> ReadAsync(LiveResearchPacket packet, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow();
            var notional = Math.Min(packet.Limits.MaximumOrderNotional ?? packet.Limits.CapitalAllocation, packet.Snapshot.Equity);
            var currentScope = JsonSerializer.Serialize(new { packet.Limits.AllowedSymbols,
                held = packet.Snapshot.Positions.Select(p => p.Symbol).Order(), notional, packet.Limits.MaximumSlippage });
            if (cached is not null && scope == currentScope && now < nextRead && now >= cached.ObservedAt) return cached;
            // One public scan per 15 minutes; model reports reuse the content-addressed document cache.
            nextRead = now.AddMinutes(15); scope = currentScope;
            var gaps = new List<string>(); var shortlist = new List<CoinScoutCandidate>();
            var scoutId = Guid.NewGuid();
            await Publish("AgentStartedTask", "working");
            try
            {
                var observations = await ObserveMarketsAsync(token);
                var ranked = CoinMarketScout.Rank(observations, packet.Limits.AllowedSymbols,
                    packet.Snapshot.Positions.Select(p => p.Symbol).ToArray(), notional);
                foreach (var candidate in ranked)
                {
                    try
                    {
                        var depth = await DepthAsync(candidate, packet.Limits.MaximumSlippage, token);
                        if (candidate.ExecutionAllowed || candidate.Held || depth >= notional)
                            shortlist.Add(candidate with { DepthQuoteNotional = depth });
                        if (depth < notional) gaps.Add($"{candidate.Symbol}: ความลึกของคำสั่งยังไม่พอกับวงเงินต่อคำสั่ง");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        if (candidate.ExecutionAllowed || candidate.Held) shortlist.Add(candidate);
                        gaps.Add($"{candidate.Symbol}: ยังตรวจความลึกของคำสั่งไม่ได้");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { gaps.Add("อ่านตลาด Bitkub ไม่สำเร็จ รอบนี้ไม่มีรายการคัดใหม่"); }
            await Publish(shortlist.Count == 0 ? "AgentFailed" : "AgentCompletedTask", shortlist.Count == 0 ? "failed" : "completed");
            var documents = new List<CoinSourceDocument>();
            var findings = new List<CoinCredibilityFinding>();
            foreach (var symbol in new[] { "BTC-THB", "ETH-THB", "SOL-THB" })
            {
                foreach (var spec in CoinSourceReader.Catalog.Where(s => s.Symbol == symbol))
                {
                    try
                    {
                        var document = await sources.ReadAsync(spec.Url, token); documents.Add(document);
                        if (document.Truncated) gaps.Add($"{symbol}: เอกสารยาวเกินขอบเขต อ่านได้บางส่วน");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception) { gaps.Add($"{symbol}: อ่าน {spec.Title} ไม่สำเร็จ"); }
                }
                var dossier = documents.Where(d => d.Source.Symbol == symbol).ToArray();
                if (dossier.Length == 0) continue;
                var result = await researcher.ReadAsync(symbol, dossier, packet.AccountContext, token);
                if (result.Finding is { } finding) findings.Add(finding);
                else gaps.Add($"{symbol}: รายงานหลักฐานยังไม่ผ่านการตรวจ");
            }
            var unsupported = shortlist.Where(c => c.Symbol is not ("BTC-THB" or "ETH-THB" or "SOL-THB")).Select(c => c.Symbol);
            if (unsupported.Any()) gaps.Add($"ยังไม่มีเอกสารที่ตรวจที่มาสำหรับ {string.Join(", ", unsupported)}");
            // Stable shortlist membership and document conclusions trigger research meetings; price changes use portfolio checkpoints.
            var changeKey = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                symbols = shortlist.Select(c => new { c.Symbol, c.ExecutionAllowed, c.Held }).OrderBy(c => c.Symbol),
                sources = documents.Select(d => d.Source.Id).Order(), findings, gaps = gaps.Distinct().Order()
            })));
            cached = new(clock.GetUtcNow(), changeKey, shortlist, documents.Select(d => d.Source).ToArray(), findings,
                gaps.Distinct().Take(12).ToArray());
            if (!CoinResearchValidation.IsValid(cached, clock.GetUtcNow())) throw new InvalidDataException("Invalid public research brief.");
            return cached;

            Task Publish(string type, string status) => events?.PublishAsync(new(type, scoutId, clock.GetUtcNow(),
                new AgentRuntimeEventPayload("news", "market-research", status, "Scanning public Bitkub markets", "ResearchDesk",
                    "Scanning public Bitkub markets", new AgentPresentation("researcher", "ResearchDesk", "market-research"), MeetingId: scoutId))
                { AccountContext = packet.AccountContext }, token) ?? Task.CompletedTask;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<CoinMarketObservation>> ObserveMarketsAsync(CancellationToken token)
    {
        using var symbols = await GetAsync("https://api.bitkub.com/api/v3/market/symbols", token);
        using var tickers = await GetAsync("https://api.bitkub.com/api/v3/market/ticker", token);
        var markets = symbols.RootElement.GetProperty("result");
        if (symbols.RootElement.GetProperty("error").GetInt32() != 0 || markets.ValueKind != JsonValueKind.Array || markets.GetArrayLength() > 1000 ||
            tickers.RootElement.ValueKind != JsonValueKind.Array || tickers.RootElement.GetArrayLength() > 1000)
            throw new InvalidDataException("Malformed public markets.");
        var metadata = new Dictionary<string, (bool Active, bool Frozen)>(StringComparer.Ordinal);
        foreach (var m in markets.EnumerateArray())
        {
            if (m.GetProperty("quote_asset").GetString() != "THB" || m.GetProperty("source").GetString() != "exchange" ||
                m.GetProperty("market_segment").GetString() != "SPOT") continue;
            var symbol = m.GetProperty("symbol").GetString()!.Replace('_', '-');
            if (!CoinResearchValidation.ValidSymbol(symbol) || !metadata.TryAdd(symbol, (m.GetProperty("status").GetString() == "active",
                m.GetProperty("freeze_buy").GetBoolean() || m.GetProperty("freeze_sell").GetBoolean() || m.GetProperty("freeze_cancel").GetBoolean())))
                throw new InvalidDataException("Invalid public market identities.");
        }
        var rows = new List<CoinMarketObservation>(); var seen = new HashSet<string>();
        foreach (var t in tickers.RootElement.EnumerateArray())
        {
            var symbol = t.GetProperty("symbol").GetString()!.ToUpperInvariant().Replace('_', '-');
            if (!metadata.TryGetValue(symbol, out var m)) continue;
            if (!seen.Add(symbol)) throw new InvalidDataException("Duplicate public ticker.");
            if (!Number(t.GetProperty("highest_bid"), out var bid) || !Number(t.GetProperty("lowest_ask"), out var ask) ||
                !Number(t.GetProperty("quote_volume"), out var volume) || !Number(t.GetProperty("percent_change"), out var change)) continue;
            rows.Add(new(symbol, m.Active, m.Frozen, bid, ask, volume, change));
        }
        return rows;
    }

    private async Task<decimal> DepthAsync(CoinScoutCandidate candidate, decimal slippage, CancellationToken token)
    {
        if (!CoinResearchValidation.ValidSymbol(candidate.Symbol)) throw new InvalidDataException();
        using var result = await GetAsync("https://api.bitkub.com/api/v3/market/depth?sym=" +
            candidate.Symbol.Replace('-', '_').ToLowerInvariant() + "&lmt=20", token);
        if (result.RootElement.GetProperty("error").GetInt32() != 0) throw new InvalidDataException();
        var book = result.RootElement.GetProperty("result");
        return Math.Min(Side(book.GetProperty("asks"), true), Side(book.GetProperty("bids"), false));

        decimal Side(JsonElement levels, bool ask)
        {
            if (levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() is < 1 or > 50) throw new InvalidDataException();
            decimal total = 0, previous = ask ? 0 : decimal.MaxValue;
            foreach (var level in levels.EnumerateArray())
            {
                if (level.ValueKind != JsonValueKind.Array || level.GetArrayLength() != 2 || !Number(level[0], out var price) ||
                    !Number(level[1], out var quantity) || price <= 0 || quantity < 0 || (ask ? price < previous : price > previous))
                    throw new InvalidDataException();
                previous = price;
                if (ask ? price <= candidate.Ask * (1 + slippage) : price >= candidate.Bid * (1 - slippage)) total += price * quantity;
            }
            return total;
        }
    }
    private async Task<JsonDocument> GetAsync(string url, CancellationToken token)
    {
        using var response = await clients.CreateClient("public-research").GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != url) throw new InvalidDataException("Unexpected public endpoint.");
        return JsonDocument.Parse(await CoinSourceReader.ReadBoundedAsync(response, 2 * 1024 * 1024, token));
    }
    private static bool Number(JsonElement e, out decimal value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Number ? e.TryGetDecimal(out value) : e.ValueKind == JsonValueKind.String &&
            decimal.TryParse(e.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
