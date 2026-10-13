using System.Text.Json;

namespace Quantara.MarketData.Infrastructure;

public sealed class MarketScanEntry
{
    public MarketScanEntry(
        string symbol,
        string? pairingId,
        decimal? last,
        decimal? change24h,
        decimal? range24h,
        decimal? volume24h,
        decimal? spreadPercent,
        bool usable,
        JsonElement sourceRule,
        JsonElement? sourceQuote
    )
    {
        this.Symbol = symbol;
        this.PairingId = pairingId;
        this.Last = last;
        this.Change24h = change24h;
        this.Range24h = range24h;
        this.Volume24h = volume24h;
        this.SpreadPercent = spreadPercent;
        this.Usable = usable;
        this.SourceRule = sourceRule;
        this.SourceQuote = sourceQuote;
    }

    public string Symbol { get; }

    public string? PairingId { get; }

    public decimal? Last { get; }

    public decimal? Change24h { get; }

    public decimal? Range24h { get; }

    public decimal? Volume24h { get; }

    public decimal? SpreadPercent { get; }

    public bool Usable { get; }

    internal JsonElement SourceRule { get; }

    internal JsonElement? SourceQuote { get; }
}
