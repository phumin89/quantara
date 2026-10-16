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
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{action} {fill.Symbol} · {fill.Quantity:0.##################} เหรียญ · {cashLabel} ฿{cash:N2}"
        );
        return new("fill:" + fill.BrokerFillId, "fill", text, now);
    }
}
