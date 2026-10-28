using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class AutonomousContextChecks
{
    private static readonly string[] warnings = ["unknown execution"];

    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new string('a', 64);
        var portfolio = JsonSerializer.Serialize(
            new
            {
                positions = Array.Empty<object>(),
                summary = new
                {
                    totalAssets = 0,
                    availableThb = 5000,
                    holdingCount = 0,
                },
                updatedAt = now,
                priceUpdatedAt = now,
                historyUpdatedAt = now,
                historyCoverage = "Fixture",
                syncing = false,
                error = (string?)null,
            }
        );
        foreach (
            var scenario in new[]
            {
                (
                    Stale: false,
                    Verified: false,
                    Version: "adaptive-intraday-v1",
                    Protocol: "ohlc-scenario-v1",
                    Risk: (decimal?)0.02m,
                    CapitalAge: 0
                ),
                (
                    Stale: false,
                    Verified: false,
                    Version: "adaptive-intraday-v2",
                    Protocol: "ohlc-scenario-v3",
                    Risk: (decimal?)0.02m,
                    CapitalAge: 0
                ),
                (
                    Stale: false,
                    Verified: true,
                    Version: "adaptive-intraday-v2",
                    Protocol: "ohlc-scenario-v3",
                    Risk: (decimal?)0.02m,
                    CapitalAge: 0
                ),
                (
                    Stale: true,
                    Verified: false,
                    Version: "adaptive-intraday-v1",
                    Protocol: "ohlc-scenario-v1",
                    Risk: (decimal?)0.02m,
                    CapitalAge: 0
                ),
                (
                    Stale: false,
                    Verified: false,
                    Version: "adaptive-intraday-v2",
                    Protocol: "ohlc-scenario-v2",
                    Risk: (decimal?)null,
                    CapitalAge: 0
                ),
                (
                    Stale: false,
                    Verified: false,
                    Version: "adaptive-intraday-v2",
                    Protocol: "ohlc-scenario-v2",
                    Risk: (decimal?)0.02m,
                    CapitalAge: 95
                ),
            }
        )
        {
            var stale = scenario.Stale;
            using var handler = new SnapshotHttpHandler(
                portfolio,
                trading: JsonSerializer.Serialize(
                    new
                    {
                        accountKey = account,
                        mode = "autoLive",
                        heartbeatAt = stale ? now.AddMinutes(-5) : now,
                        ownerToken = "never-expose",
                        paper = new { positions = Array.Empty<object>(), orders = Array.Empty<object>() },
                        live = new
                        {
                            positions = new[]
                            {
                                new
                                {
                                    symbol = "BTC_THB",
                                    quantity = "0.25",
                                    costThb = "1000",
                                    openedAt = now,
                                    peakBid = "4500",
                                    exitPolicy = "structural",
                                    stopPrice = "4000",
                                    targetPrice = "5000",
                                    trailingDistance = "100",
                                    trailingActive = true,
                                    timeExitAt = now.AddHours(1),
                                },
                            },
                            orders = new[]
                            {
                                new
                                {
                                    symbol = "BTC_THB",
                                    side = "sell",
                                    remaining = "0.1",
                                    limitPrice = "4900",
                                    createdAt = now,
                                    expiresAt = now.AddMinutes(10),
                                    status = "submitted",
                                },
                            },
                        },
                        rules = new
                        {
                            version = scenario.Version,
                            maxDataAgeSeconds = 90,
                            adaptive = new
                            {
                                risk = new
                                {
                                    profile = "aggressive",
                                    maximumExposureFraction = 0.8m,
                                    maximumConcentrationFraction = 0.5m,
                                    riskToStopFraction = scenario.Risk,
                                    atrMultiplier = 1.5m,
                                    maximumSpreadFraction = 0.01m,
                                    estimatedFeeRate = 0.0025m,
                                    maximumDataAgeSeconds = 90,
                                    maximumSignalAgeSeconds = 600,
                                },
                                dailyLossFraction = 0.05m,
                                includeFutureDeposits = true,
                            },
                        },
                        fault = (string?)null,
                        autonomy = new
                        {
                            dailyProfitTargetThb = "500",
                            realizedNetTodayThb = (string?)null,
                            totalMarkets = 100,
                            eligibleMarkets = 90,
                            checkedMarkets = 32,
                            activity = "Scanning markets",
                            capital = new
                            {
                                observedAt = now.AddSeconds(-scenario.CapitalAge),
                                availableThb = "5029.43",
                                equityThb = "5029.43",
                                exposureThb = "0",
                                reservedThb = "0",
                                blocker = (string?)null,
                            },
                            opportunityReview = new
                            {
                                protocol = "observed-book-v1",
                                days = new[]
                                {
                                    new
                                    {
                                        date = "2026-10-19",
                                        captured = 13,
                                        closed = 3,
                                        waiting = 5,
                                        open = 1,
                                        expired = 5,
                                        closedUnknown = 0,
                                        unknown = 0,
                                        meanNetReturnFraction = -0.005m,
                                    },
                                },
                            },
                            candidates = Enumerable
                                .Range(0, 12)
                                .Select(_ => new
                                {
                                    symbol = "ETH",
                                    at = now,
                                    reason = "No entry signal",
                                    strategyVersion = scenario.Version,
                                    score = 3m,
                                }),
                            readiness = new
                            {
                                readyMarkets = 5,
                                unavailableMarkets = 2,
                                frames = new[]
                                {
                                    new
                                    {
                                        symbol = "ETH",
                                        timeframeSeconds = 900,
                                        status = "ready",
                                        gapCause = (string?)null,
                                        checkedAt = now,
                                        reason = (string?)null,
                                    },
                                    new
                                    {
                                        symbol = "BTC",
                                        timeframeSeconds = 3600,
                                        status = "unavailable",
                                        gapCause = (string?)"unknown",
                                        checkedAt = now,
                                        reason = (string?)"missing",
                                    },
                                },
                            },
                            funnel = new
                            {
                                date = "2026-10-18",
                                coverage = "7d",
                                signals = 12,
                                submittedOrders = 2,
                                filledOrders = 1,
                                closedTrades = 0,
                                netThb = (string?)null,
                                blockedReasons = new[]
                                {
                                    new { reason = "unknownFill", count = 3 },
                                    new { reason = "stale", count = 1 },
                                },
                            },
                            evaluation = new
                            {
                                protocol = scenario.Protocol,
                                verifiedProfit = scenario.Verified,
                                observedAt = now,
                                rulesHash = "hash",
                                warnings = AutonomousContextChecks.warnings,
                                metrics = new object[]
                                {
                                    new
                                    {
                                        segment = "training",
                                        symbol = "ETH",
                                        timeframeSeconds = 900,
                                        strategy = "breakout",
                                        samples = 1966,
                                        trades = 2,
                                        unknownFills = 1,
                                        netThb = (decimal?)null,
                                        feesThb = 2m,
                                        maxDrawdownThb = (decimal?)null,
                                        cashBenchmarkThb = 0m,
                                        insufficientSamples = false,
                                    },
                                    new
                                    {
                                        segment = "validation",
                                        symbol = "ETH",
                                        timeframeSeconds = 900,
                                        strategy = "breakout",
                                        samples = 844,
                                        trades = 1,
                                        unknownFills = 1,
                                        netThb = (decimal?)null,
                                        feesThb = 1m,
                                        maxDrawdownThb = (decimal?)null,
                                        cashBenchmarkThb = 0m,
                                        insufficientSamples = false,
                                    },
                                },
                            },
                        },
                    }
                )
            );
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://portfolio.invalid") };
            var result = await new PortfolioSnapshotReader(client).Read(CancellationToken.None);
            using var document = JsonDocument.Parse(result);
            var performance = document.RootElement.GetProperty("tradingPerformance");
            if (
                performance.GetProperty("available").GetBoolean() == stale
                || handler.TradingReads != 1
                || result.Contains("never-expose", StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "Research must project one fresh account-bound trading read and no authority fields."
                );
            }

            if (
                !stale
                && scenario.Risk is not null
                && (
                    performance.GetProperty("mode").GetString() != "autoLive"
                    || performance.GetProperty("dailyProfitTargetThb").GetDecimal() != 500
                    || performance.GetProperty("realizedNetTodayThb").ValueKind != JsonValueKind.Null
                    || performance.GetProperty("candidateReasons").GetArrayLength() != 8
                    || performance.GetProperty("candidateObservations").GetArrayLength() != 8
                    || performance.GetProperty("candidateObservations")[0].GetProperty("symbol").GetString() != "ETH"
                    || performance.GetProperty("candidateObservations")[0].GetProperty("at").GetDateTimeOffset() != now
                    || performance.GetProperty("strategy").GetProperty("version").GetString() != scenario.Version
                    || performance.GetProperty("strategy").GetProperty("sizing").GetProperty("fixedOrderThb").ValueKind
                        != JsonValueKind.Null
                    || (
                        scenario.CapitalAge == 0
                            ? performance.GetProperty("capital").GetProperty("availableThb").GetDecimal() != 5029.43m
                            : performance.GetProperty("capital").GetProperty("availableThb").ValueKind
                                != JsonValueKind.Null
                    )
                    || performance.GetProperty("capital").GetProperty("observedAt").GetDateTimeOffset()
                        != now.AddSeconds(-scenario.CapitalAge)
                    || performance.GetProperty("capital").GetProperty("maximumAgeSeconds").GetInt32() != 90
                    || performance
                        .GetProperty("opportunityReview")
                        .GetProperty("days")[0]
                        .GetProperty("expired")
                        .GetInt32() != 5
                    || !performance.GetProperty("opportunityReview").GetProperty("waitingIncludesOpen").GetBoolean()
                    || !performance.GetProperty("positionEvidence").GetProperty("available").GetBoolean()
                    || performance.GetProperty("positionEvidence").GetProperty("executionAuthority").GetBoolean()
                    || performance
                        .GetProperty("positionEvidence")
                        .GetProperty("positions")[0]
                        .GetProperty("exitPolicy")
                        .GetString() != "structural"
                    || performance
                        .GetProperty("positionEvidence")
                        .GetProperty("positions")[0]
                        .GetProperty("stopPrice")
                        .GetDecimal() != 4000
                    || performance
                        .GetProperty("positionEvidence")
                        .GetProperty("orders")[0]
                        .GetProperty("expiresAt")
                        .GetDateTimeOffset() != now.AddMinutes(10)
                    || performance.GetProperty("pnlScope").GetProperty("basis").GetString() != "selected-engine-ledger"
                    || performance.GetProperty("readiness").GetProperty("readyMarkets").GetInt32() != 5
                    || performance
                        .GetProperty("readiness")
                        .GetProperty("frames")[0]
                        .GetProperty("timeframeSeconds")
                        .GetInt32() != 900
                    || performance
                        .GetProperty("readiness")
                        .GetProperty("frames")[1]
                        .GetProperty("timeframeSeconds")
                        .GetInt32() != 3600
                    || performance
                        .GetProperty("funnel")
                        .GetProperty("blockedReasons")[0]
                        .GetProperty("reason")
                        .GetString() != "unknownFill"
                    || performance.GetProperty("evaluation").GetProperty("metrics").GetArrayLength()
                        != (scenario.Verified ? 0 : 1)
                    || !scenario.Verified
                        && performance
                            .GetProperty("evaluation")
                            .GetProperty("metrics")[0]
                            .GetProperty("netThb")
                            .ValueKind != JsonValueKind.Null
                )
            )
            {
                throw new InvalidOperationException(
                    "Actual adaptive context must preserve unknown profit and bound candidate details."
                );
            }

            var strategy = document.RootElement.GetProperty("tradingStrategy");
            if (
                !stale
                && !scenario.Verified
                && performance.GetProperty("evaluation").GetProperty("protocol").GetString() != scenario.Protocol
            )
            {
                throw new InvalidOperationException(
                    "Research preserves both retained and precision-aware replay protocols."
                );
            }

            if (
                scenario.Risk is null
                    ? strategy.ValueKind != JsonValueKind.Null
                    : strategy.GetProperty("version").GetString() != scenario.Version
            )
            {
                throw new InvalidOperationException(
                    "Saved strategy survives stale performance; incomplete policy never receives invented defaults."
                );
            }

            if (
                !stale
                && scenario.Verified
                && performance.GetProperty("evaluation").GetProperty("protocol").ValueKind != JsonValueKind.Null
            )
            {
                throw new InvalidOperationException("Verified evaluation payloads must be rejected.");
            }
        }

        Console.WriteLine("Autonomous research context checks passed.");
    }
}
