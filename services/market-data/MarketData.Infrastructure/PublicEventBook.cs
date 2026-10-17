using System.Globalization;
using System.Text.Json;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Infrastructure;

public sealed class PublicEventBook
{
    private readonly string symbol;

    private readonly int pairingId;

    private readonly JsonElement rule;

    private (decimal Price, decimal Quantity, DateTimeOffset At)? bid;

    private (decimal Price, decimal Quantity, DateTimeOffset At)? ask;

    public PublicEventBook(string symbol, int pairingId, JsonElement rule)
    {
        this.symbol = symbol;
        this.pairingId = pairingId;
        this.rule = rule.Clone();
    }

    public MarketEventBook? Apply(string json, DateTimeOffset observedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("pairing_id", out var identity) && Number(identity) != this.pairingId)
            {
                return null;
            }

            var kind = root.GetProperty("event").GetString();
            var data = root.GetProperty("data");
            switch (kind)
            {
                case "bidschanged":
                    this.bid = Side(data, true, observedAt, false);
                    break;
                case "askschanged":
                    this.ask = Side(data, false, observedAt, false);
                    break;
                case "tradeschanged" when data.ValueKind == JsonValueKind.Array && data.GetArrayLength() == 3:
                    this.bid = Side(data[1], true, observedAt, false);
                    this.ask = Side(data[2], false, observedAt, false);
                    break;
                case "depthchanged" when data.ValueKind == JsonValueKind.Object:
                    this.bid = Side(data.GetProperty("bids"), true, observedAt, true);
                    this.ask = Side(data.GetProperty("asks"), false, observedAt, true);
                    break;
                default:
                    return null;
            }

            if (
                this.bid is not { } bid
                || this.ask is not { } ask
                || ask.Price < bid.Price
                || observedAt - bid.At > TimeSpan.FromSeconds(90)
                || observedAt - ask.At > TimeSpan.FromSeconds(90)
            )
            {
                return null;
            }

            var priceStep = Number(this.rule.GetProperty("price_step"));
            var minimum = Number(this.rule.GetProperty("min_quote_size"));
            var priceScale = Scale(this.rule, "price_scale");
            var baseScale = Scale(this.rule, "base_asset_scale");
            var quoteScale = Scale(this.rule, "quote_asset_scale");
            if (priceStep <= 0 || minimum <= 0 || decimal.Round(priceStep, priceScale) != priceStep)
            {
                return null;
            }

            var freezeBuy = bool.Parse(this.rule.GetProperty("freeze_buy").ToString());
            var freezeSell = bool.Parse(this.rule.GetProperty("freeze_sell").ToString());
            return new(
                this.symbol,
                bid.At < ask.At ? bid.At : ask.At,
                bid.Price,
                ask.Price,
                bid.Quantity,
                ask.Quantity,
                [],
                minimum,
                priceStep,
                priceScale,
                baseScale,
                quoteScale,
                !freezeBuy && !freezeSell,
                !freezeSell
            );
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or InvalidDataException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
                        or OverflowException
            )
        {
            this.bid = null;
            this.ask = null;
            return null;
        }
    }

    public void Reset()
    {
        this.bid = null;
        this.ask = null;
    }

    private static (decimal Price, decimal Quantity, DateTimeOffset At)? Side(
        JsonElement levels,
        bool buy,
        DateTimeOffset at,
        bool depth
    )
    {
        if (levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() is < 1 or > 100)
        {
            return null;
        }

        var rows = levels
            .EnumerateArray()
            .Select(level =>
            {
                if (depth && level.ValueKind == JsonValueKind.Object)
                {
                    return (
                        Price: Number(level.GetProperty("price")),
                        Quantity: Number(level.GetProperty("base_volume"))
                    );
                }

                if (level.ValueKind != JsonValueKind.Array || level.GetArrayLength() < (depth ? 2 : 3))
                {
                    throw new InvalidDataException("Invalid public order book level.");
                }

                return (Price: Number(level[depth ? 0 : 1]), Quantity: Number(level[depth ? 1 : 2]));
            })
            .ToArray();
        if (rows.Any(row => row.Price <= 0 || row.Quantity <= 0))
        {
            return null;
        }

        var price = buy ? rows.Max(row => row.Price) : rows.Min(row => row.Price);
        return (price, rows.Where(row => row.Price == price).Sum(row => row.Quantity), at);
    }

    private static decimal Number(JsonElement value) =>
        decimal.Parse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int Scale(JsonElement rule, string property)
    {
        var scale = Number(rule.GetProperty(property));
        return scale is >= 0 and <= 18 && scale == decimal.Truncate(scale)
            ? (int)scale
            : throw new FormatException("Invalid precision.");
    }
}
