using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public sealed class TradingProjectionChecks(TradingState state) : IReadOnlyTradingStore
{
    public Task<TradingState> Read(CancellationToken cancellationToken) => Task.FromResult(state);

    public static async Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var initial = TradingStateFactory.Create(now);
        var account = new AccountContext(new string('a', 64));
        var options = new TradingControlOptions("owner-token") { LiveActivationAvailable = true };
        var projection = new TradingOverviewProjection(account, options, new FixedTimeProvider(now));
        var report = new TradingDailyReport(
            "2026-10-16",
            TradingMode.Paper,
            1,
            now,
            "daily report",
            "prepared",
            1000,
            1000.123456789012345m,
            null,
            "fingerprint"
        );
        var current = initial with
        {
            Mode = TradingMode.AutoLive,
            Mandate = new TradingMandate(
                Guid.NewGuid(),
                account.AccountKey,
                initial.RulesHash,
                now,
                now.AddHours(1),
                null
            ),
            Reports = [report],
            Paper = initial.Paper with
            {
                CashThb = 1000.123456789012345m,
                Equity = [new TradingEquityMark(now, 1000.123456789012345m, 1000)],
            },
        };
        var managed = current with
        {
            Rules = current.Rules with { Version = "adaptive-intraday-v2", Symbols = [], Adaptive = new() },
            Live = current.Live with
            {
                Positions =
                [
                    new TradingHolding("ETH", 10, 1000, now, 112)
                    {
                        StopPrice = 95,
                        TargetPrice = 120,
                        TrailingDistance = 3,
                    },
                ],
                Orders =
                [
                    new TradingIntent(
                        Guid.NewGuid(),
                        "ETH",
                        Quantara.Portfolio.Domain.TradeSide.Sell,
                        10,
                        120,
                        10,
                        now,
                        0,
                        current.RulesHash,
                        "fixture",
                        "submitted"
                    ),
                ],
            },
            Autonomy = new(
                now,
                now,
                2,
                2,
                [new("OLD", now.AddMinutes(-20), "stale", null, 99), new("ETH", now, "fresh", "breakout-v2-15m", 1)],
                null,
                "Monitoring",
                now
            ),
        };
        using var managedView = JsonDocument.Parse(projection.Serialize(managed));
        var managedPosition = managedView.RootElement.GetProperty("live").GetProperty("positions")[0];
        Check(
            managedPosition.GetProperty("stopPrice").GetString() == "95"
                && managedPosition.GetProperty("targetPrice").GetString() == "120"
                && managedPosition.GetProperty("trailingDistance").GetString() == "3"
                && managedPosition.GetProperty("timeExitAt").GetDateTimeOffset()
                    == now.AddMinutes(managed.Rules.TimeExitMinutes)
                && managedView
                    .RootElement.GetProperty("live")
                    .GetProperty("orders")[0]
                    .GetProperty("expiresAt")
                    .GetDateTimeOffset() == now.AddMinutes(managed.Rules.OrderLifetimeMinutes)
                && managedView.RootElement.GetProperty("autonomy").GetProperty("candidates").GetArrayLength() == 1,
            "Read-only projection preserves actual exit levels, deadlines and only fresh candidates."
        );
        using var result = JsonDocument.Parse(
            await new GetTradingOverviewHandler(new TradingProjectionChecks(current), projection).Handle(
                new GetTradingOverviewQuery(),
                CancellationToken.None
            )
        );
        var root = result.RootElement;
        Check(
            root.GetProperty("accountKey").GetString() == account.AccountKey
                && root.GetProperty("mode").GetString() == "autoLive"
                && root.GetProperty("liveActivationAvailable").GetBoolean(),
            "Trading overview must preserve account provenance, exact UI mode and configured activation availability."
        );
        Check(
            root.GetProperty("paper").GetProperty("cashThb").GetString() == "1000.123456789012345"
                && root.GetProperty("paper").GetProperty("equityThb").GetString() == "1000.123456789012345"
                && root.GetProperty("live").GetProperty("realizedNetThb").ValueKind == JsonValueKind.Null,
            "Decimal strings must remain lossless and unfunded live profit must stay unknown."
        );
        Check(
            root.GetProperty("mandate").GetProperty("accountKey").GetString() == account.AccountKey
                && root.GetProperty("rules").GetProperty("allocatedThb").ValueKind == JsonValueKind.Number,
            "Mandate provenance and numeric rule contract must survive projection."
        );
        using var reports = JsonDocument.Parse(
            await new GetTradingReportsHandler(new TradingProjectionChecks(current)).Handle(
                new GetTradingReportsQuery(),
                CancellationToken.None
            )
        );
        Check(
            reports.RootElement.GetProperty("reports")[0].GetProperty("endingEquityThb").GetString()
                == "1000.123456789012345"
                && reports.RootElement.GetProperty("reports")[0].GetProperty("realizedNetThb").ValueKind
                    == JsonValueKind.Null,
            "Read-only reports must retain exact amounts and unknown realized profit."
        );

        foreach (
            var mark in new[]
            {
                new TradingEquityMark(now.AddSeconds(-initial.Rules.MaxDataAgeSeconds - 1), 1000, 1000),
                new TradingEquityMark(now.AddSeconds(1), 1000, 1000),
                new TradingEquityMark(now, 1000, 999),
                new TradingEquityMark(now, null, 1000),
            }
        )
        {
            using var unavailable = JsonDocument.Parse(
                projection.Serialize(initial with { Paper = initial.Paper with { Equity = [mark] } })
            );
            Check(
                unavailable.RootElement.GetProperty("paper").GetProperty("equityThb").ValueKind == JsonValueKind.Null
                    && unavailable.RootElement.GetProperty("paper").GetProperty("unrealizedThb").ValueKind
                        == JsonValueKind.Null,
                "Stale, future, mismatched or unknown equity must never become a fabricated valuation."
            );
        }

        using var noCredentials = JsonDocument.Parse(
            new TradingOverviewProjection(
                account,
                new TradingControlOptions("owner-token"),
                new FixedTimeProvider(now)
            ).Serialize(initial)
        );
        using var noToken = JsonDocument.Parse(
            new TradingOverviewProjection(
                account,
                new TradingControlOptions("") { LiveActivationAvailable = true },
                new FixedTimeProvider(now)
            ).Serialize(initial)
        );
        Check(
            !noCredentials.RootElement.GetProperty("liveActivationAvailable").GetBoolean()
                && !noToken.RootElement.GetProperty("liveActivationAvailable").GetBoolean(),
            "Both owner token and broker configuration are required for activation availability."
        );
        Console.WriteLine("Trading projection checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
