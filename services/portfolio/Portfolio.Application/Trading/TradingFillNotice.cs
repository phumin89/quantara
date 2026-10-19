using System.Globalization;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingFillNotice
{
    public static TradingAlert Create(TradingFill fill, DateTimeOffset now)
    {
        var buy = fill.Side == TradeSide.Buy;
        var action = buy ? "🟢 ซื้อ" : "🔴 ขาย";
        var cashLabel = buy ? "จ่าย" : "รับ";
        var cash = buy ? fill.GrossThb + fill.FeeThb : fill.GrossThb - fill.FeeThb;
        var cashText = cash.ToString("N2", CultureInfo.InvariantCulture);
        var quantity = FormatQuantity(
            fill.QuantityScale is { } scale
                ? decimal.Round(fill.Quantity, scale, MidpointRounding.ToZero)
                : fill.Quantity
        );
        var text = string.Join(
            Environment.NewLine,
            $"{action} {fill.Symbol} แล้ว",
            $"จำนวน {quantity} {fill.Symbol}",
            $"ราคา ฿{FormatPrice(fill.Price)}",
            $"{cashLabel}{(buy ? "รวม" : "สุทธิ")} ฿{cashText}"
        );
        return new("fill:" + fill.BrokerFillId, "fill", text, now);
    }

    private static string FormatQuantity(decimal quantity)
    {
        var rounded = decimal.Round(quantity, 8, MidpointRounding.ToZero);
        if (quantity > 0 && rounded == 0)
        {
            return quantity.ToString("0.##############################", CultureInfo.InvariantCulture);
        }

        var text = rounded.ToString("0.########", CultureInfo.InvariantCulture);
        return rounded != quantity ? "≈" + text : text;
    }

    private static string FormatPrice(decimal price)
    {
        var rounded = decimal.Round(price, 8, MidpointRounding.ToZero);
        return price > 0 && rounded == 0
            ? price.ToString("0.##############################", CultureInfo.InvariantCulture)
            : rounded.ToString("#,##0.########", CultureInfo.InvariantCulture);
    }
}
