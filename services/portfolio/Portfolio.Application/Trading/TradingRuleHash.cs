using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingRuleHash
{
    public static string Compute(TradingRuleSet rules) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(rules)))).ToLowerInvariant();

    private static string Serialize(TradingRuleSet rules) =>
        rules.Version == "ema20-50-breakout-v1" && rules.Adaptive is null
            ? JsonSerializer.Serialize(
                new
                {
                    rules.Version,
                    rules.Symbols,
                    rules.AllocatedThb,
                    rules.PerOrderThb,
                    rules.DailyLossThb,
                    rules.MaxSpreadBps,
                    rules.SlippageBps,
                    rules.TakeProfitBps,
                    rules.StopLossBps,
                    rules.TrailingStopBps,
                    rules.FeeBps,
                    rules.VolumeRatio,
                    rules.MaxPositions,
                    rules.MaxDataAgeSeconds,
                    rules.TimeExitMinutes,
                    rules.CooldownMinutes,
                    rules.OrderLifetimeMinutes,
                }
            )
        : rules.Version == "adaptive-intraday-v3" ? JsonSerializer.Serialize(rules)
        : JsonSerializer.Serialize(
            new
            {
                rules.Version,
                rules.Symbols,
                rules.AllocatedThb,
                rules.PerOrderThb,
                rules.DailyLossThb,
                rules.MaxSpreadBps,
                rules.SlippageBps,
                rules.TakeProfitBps,
                rules.StopLossBps,
                rules.TrailingStopBps,
                rules.FeeBps,
                rules.VolumeRatio,
                rules.MaxPositions,
                rules.MaxDataAgeSeconds,
                rules.TimeExitMinutes,
                rules.CooldownMinutes,
                rules.OrderLifetimeMinutes,
                rules.Adaptive,
            }
        );
}
