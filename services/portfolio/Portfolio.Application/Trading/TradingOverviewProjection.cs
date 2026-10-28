using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingOverviewProjection(
    IAccountContext account,
    TradingControlOptions options,
    TimeProvider time,
    TradingEventDispatcher? events = null,
    TradingNotificationDiagnostics? notifications = null
)
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<RiskProfile>(JsonNamingPolicy.CamelCase, false) },
        TypeInfoResolver = TradingJsonCompatibility.CreateResolver(),
    };

    public string Serialize(TradingState state, PortfolioGoalCheckpoint? goal = null) =>
        JsonSerializer.Serialize(
            new
            {
                accountKey = account.AccountKey,
                objective = PortfolioGoalAccounting.View(account.AccountKey, goal, time.GetUtcNow()),
                mode = Mode(state.Mode),
                state.Rules,
                state.RulesHash,
                aiPolicy = state.AiPolicy is not { } ai
                    ? null
                    : new
                    {
                        enabled = TradingAuthority.Valid(state, account.AccountKey, time.GetUtcNow())
                            && ai.RevokedAt is null,
                        ai.CapitalCeilingFraction,
                        ai.ExpiresAt,
                        ai.OriginalRulesHash,
                        ai.EffectiveRulesHash,
                        status = ai.RevokedAt is not null || time.GetUtcNow() >= ai.ExpiresAt ? "stopped" : ai.Status,
                        ai.Reason,
                        lastChangedAt = state
                            .PolicyChanges.Where(change => change.Authorization.MandateId == ai.MandateId)
                            .Select(change => (DateTimeOffset?)change.AppliedAt)
                            .LastOrDefault(),
                    },
                mandate = state.Mandate is null
                    ? null
                    : new
                    {
                        id = state.Mandate.Id.ToString("D"),
                        state.Mandate.AccountKey,
                        state.Mandate.RulesHash,
                        state.Mandate.CreatedAt,
                        state.Mandate.ExpiresAt,
                        state.Mandate.RevokedAt,
                    },
                state.EmergencyStopped,
                state.HeartbeatAt,
                state.Fault,
                realtime = events?.Snapshot(),
                autonomy = Autonomy(state, time.GetUtcNow()),
                notifications = this.Notifications(state),
                exitStudy = this.ExitStudy(state),
                liveActivationAvailable = options.LiveActivationAvailable
                    && (state.Rules.ExitPolicy is null || options.AdaptiveExitLiveAvailable)
                    && !string.IsNullOrWhiteSpace(options.OwnerToken),
                paper = Book(state.Paper, state.Rules, time.GetUtcNow()),
                live = Book(state.Live, state.Rules, time.GetUtcNow()),
                reports = Reports(state),
            },
            TradingOverviewProjection.jsonOptions
        );

    public static string SerializeReports(TradingState state) =>
        JsonSerializer.Serialize(new { reports = Reports(state) }, TradingOverviewProjection.jsonOptions);

    private static string Mode(TradingMode mode) => JsonNamingPolicy.CamelCase.ConvertName(mode.ToString());

    private object Notifications(TradingState state)
    {
        var alerts = state.Alerts.Where(alert => state.Rules.Adaptive is null || alert.Kind == "fill").ToArray();
        var pending = alerts.Where(alert => alert.Status == "pending").ToArray();
        var now = time.GetUtcNow();
        var oldest = pending.Select(alert => (DateTimeOffset?)alert.CreatedAt).Min();
        var unknown = alerts.Count(alert =>
            alert.Status == "unknown" || alert.Status == "sending" && TradingAlertDelivery.IsStale(alert, now)
        );
        var blocked = (notifications?.Blocked ?? 0) + alerts.Count(alert => alert.Status == "rejected");
        return new
        {
            pending = pending.Length,
            sending = alerts.Count(alert => alert.Status == "sending" && !TradingAlertDelivery.IsStale(alert, now)),
            sent = alerts.Count(alert => alert.Status == "sent"),
            unknown,
            blocked,
            dashboardAvailable = notifications?.DashboardAvailable == true,
            oldestPendingAt = oldest,
            state = notifications?.Configured != true ? "unconfigured"
            : unknown + blocked > 0 ? "needsReview"
            : oldest is { } at && now - at > TimeSpan.FromMinutes(1) ? "delayed"
            : pending.Length > 0 || alerts.Any(alert => alert.Status == "sending") ? "pending"
            : "ready",
        };
    }

    private object ExitStudy(TradingState state)
    {
        var samples = state.ExitStudies.Where(sample => sample.SourceRulesHash == state.RulesHash).ToArray();
        var live = samples.Where(sample => sample.SourceMode == "live").ToArray();
        var paired = live.Where(sample => sample.HypotheticalNetThb.HasValue && sample.BaselineNetThb.HasValue)
            .ToArray();
        var started = live.Select(sample => (DateTimeOffset?)sample.CreatedAt).Min();
        var enough = started is { } at && time.GetUtcNow() - at >= TimeSpan.FromDays(14) && paired.Length >= 30;
        return new
        {
            protocol = "paired-exit-v1",
            status = enough ? "readyForReview" : "collecting",
            verifiedProfit = false,
            startedAt = started,
            observedAt = time.GetUtcNow(),
            liveSamples = live.Length,
            paperSamples = samples.Length - live.Length,
            pairedClosed = paired.Length,
            unknown = samples.Count(sample =>
                sample.HypotheticalNetThb is null && sample.Status is "unknown" or "baseline"
            ),
            minimumDays = 14,
            minimumClosed = 30,
            hypotheticalNetThb = paired.Length == 0
                ? null
                : Decimal(paired.Sum(sample => sample.HypotheticalNetThb.GetValueOrDefault())),
            baselineNetThb = paired.Length == 0
                ? null
                : Decimal(paired.Sum(sample => sample.BaselineNetThb.GetValueOrDefault())),
        };
    }

    private static object? Autonomy(TradingState state, DateTimeOffset now)
    {
        if (state.Rules.Adaptive is not { } policy || state.Autonomy is not { } status)
        {
            return null;
        }

        var today = now.ToOffset(TimeSpan.FromHours(7)).Date;
        var ledger = state.Mode == TradingMode.Paper ? state.Paper : state.Live;
        var sells = ledger
            .Fills.Where(fill =>
                fill.Side == TradeSide.Sell && fill.OccurredAt.ToOffset(TimeSpan.FromHours(7)).Date == today
            )
            .ToArray();
        var realizedNetToday =
            sells.Any(fill => fill.RealizedNetThb is null)
            || ledger.ExternalSales.Any(sale =>
                sale.Allocation.Execution.OccurredAt.ToOffset(TimeSpan.FromHours(7)).Date == today
            )
                ? null
                : Decimal(sells.Sum(fill => fill.RealizedNetThb ?? 0));
        return new
        {
            status.ObservedAt,
            status.UniverseAt,
            status.TotalMarkets,
            status.EligibleMarkets,
            checkedMarkets = status.Markets.Count,
            evaluation = state.Evaluation is { } evaluation && evaluation.RulesHash == state.RulesHash
                ? evaluation
                : null,
            readiness = status.History is null
                ? null
                : new
                {
                    readyMarkets = status.Markets.Count(market =>
                        status.History.Any(item => item.Symbol == market.Symbol && item.Status == "ready")
                    ),
                    unavailableMarkets = status.Markets.Count(market =>
                        !status.History.Any(item => item.Symbol == market.Symbol && item.Status == "ready")
                    ),
                    frames = status.History.OrderByDescending(item => item.CheckedAt).Take(48),
                },
            funnel = new
            {
                date = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                coverage = state.Decisions.Any(item =>
                    item.Mode == (state.Mode == TradingMode.Paper ? TradingMode.Paper : TradingMode.AutoLive)
                    && item.At.ToOffset(TimeSpan.FromHours(7)).Date == today
                    && item.SignalAction is null
                )
                    ? "retained"
                    : "complete",
                signals = state.Decisions.Count(item =>
                    item.Mode == (state.Mode == TradingMode.Paper ? TradingMode.Paper : TradingMode.AutoLive)
                    && item.At.ToOffset(TimeSpan.FromHours(7)).Date == today
                    && (item.SignalAction ?? item.Action) is TradingAction.Buy or TradingAction.Sell
                ),
                submittedOrders = ledger.Orders.Count(order =>
                    order.CreatedAt.ToOffset(TimeSpan.FromHours(7)).Date == today && order.BrokerOrderId is not null
                ),
                filledOrders = ledger
                    .Fills.Where(fill => fill.OccurredAt.ToOffset(TimeSpan.FromHours(7)).Date == today)
                    .Select(fill => fill.IntentId)
                    .Distinct()
                    .Count(),
                closedTrades = ledger.Orders.Count(order =>
                    order.Side == TradeSide.Sell
                    && order.Status == "filled"
                    && order.LastInspectedAt?.ToOffset(TimeSpan.FromHours(7)).Date == today
                ),
                netThb = realizedNetToday,
                blockedReasons = state
                    .Decisions.Where(item =>
                        item.Mode == (state.Mode == TradingMode.Paper ? TradingMode.Paper : TradingMode.AutoLive)
                        && item.At.ToOffset(TimeSpan.FromHours(7)).Date == today
                        && item.Action == TradingAction.Wait
                    )
                    .GroupBy(item => item.Reason, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .Take(10)
                    .Select(group => new { reason = group.Key, count = group.Count() }),
            },
            status.Activity,
            status.NextCheckAt,
            capital = status.Capital is not { } capital
                ? null
                : new
                {
                    capital.ObservedAt,
                    availableThb = NullableDecimal(capital.SpendableThb),
                    equityThb = NullableDecimal(capital.EquityThb),
                    exposureThb = NullableDecimal(capital.ExposureThb),
                    reservedThb = NullableDecimal(capital.ReservedThb),
                    capital.Blocker,
                },
            candidates = TradingWatchSelection
                .Candidates(state, now)
                .Select(item => new
                {
                    item.Symbol,
                    item.At,
                    item.Reason,
                    item.StrategyVersion,
                    score = NullableDecimal(item.Score),
                }),
            dailyProfitTargetThb = Decimal(policy.DailyProfitTargetThb),
            opportunityReview = new
            {
                protocol = TradingShadowProtocol.Version,
                sampleLimit = 8,
                assumptions = "Sampled hypothetical entries using subsequent observed books, visible liquidity and fees. Budget is ten exchange minimum notionals per sample. Seven-day model window; not actual fills or missed account profit.",
                days = TradingOpportunityReview.Days(state, now),
            },
            realizedNetTodayThb = realizedNetToday,
        };
    }

    private static string Decimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string? NullableDecimal(decimal? value) => value.HasValue ? Decimal(value.Value) : null;

    private static object[] Reports(TradingState state) =>
        state
            .Reports.OrderByDescending(report => report.Date)
            .ThenByDescending(report => report.Revision)
            .Select(report =>
                (object)
                    new
                    {
                        report.Date,
                        mode = Mode(report.Mode),
                        report.Revision,
                        report.CreatedAt,
                        report.Text,
                        status = report.Status switch
                        {
                            "sent" => "sent",
                            "local" => "local",
                            "prepared" => "prepared",
                            "unconfigured" or "pending" => "pending",
                            _ => "uncertain",
                        },
                        startingEquityThb = NullableDecimal(report.StartingEquityThb),
                        endingEquityThb = NullableDecimal(report.EndingEquityThb),
                        realizedNetThb = NullableDecimal(report.RealizedNetThb),
                    }
            )
            .ToArray();

    private static object Book(TradingLedger ledger, TradingRuleSet rules, DateTimeOffset now)
    {
        var mark = ledger.Equity.OrderBy(item => item.At).LastOrDefault();
        decimal? equity =
            mark is { EquityThb: not null }
            && mark.At <= now
            && now - mark.At <= TimeSpan.FromSeconds(rules.MaxDataAgeSeconds)
            && mark.FundingThb == ledger.FundingThb
                ? mark.EquityThb
                : null;
        var sells = ledger.Fills.Where(fill => fill.Side == TradeSide.Sell).ToArray();
        var known = ledger.FundingThb > 0 && ledger.ExternalSales.Count == 0;
        decimal? realizedGross =
            known && sells.All(fill => fill.RealizedGrossThb.HasValue)
                ? sells.Sum(fill => fill.RealizedGrossThb.GetValueOrDefault())
                : null;
        decimal? realizedNet =
            known && sells.All(fill => fill.RealizedNetThb.HasValue)
                ? sells.Sum(fill => fill.RealizedNetThb.GetValueOrDefault())
                : null;
        return new
        {
            cashThb = Decimal(ledger.CashThb),
            equityThb = NullableDecimal(equity),
            realizedGrossThb = NullableDecimal(realizedGross),
            realizedNetThb = NullableDecimal(realizedNet),
            feesThb = Decimal(ledger.Fills.Sum(fill => fill.FeeThb)),
            unrealizedThb = NullableDecimal(
                equity - ledger.CashThb - ledger.Positions.Sum(position => position.CostThb)
            ),
            positions = ledger
                .Positions.Where(position => position.Quantity > 0)
                .Select(position => new
                {
                    position.Symbol,
                    quantity = Decimal(position.Quantity),
                    costThb = Decimal(position.CostThb),
                    position.OpenedAt,
                    peakBid = Decimal(position.PeakBid),
                    exitPolicy = rules.Adaptive is not null && position.StopPrice is not null
                        ? "structural"
                        : "percentage",
                    stopPrice = NullableDecimal(
                        rules.Adaptive is not null && position.StopPrice is not null
                            ? position.StopPrice
                            : position.AverageCost * (1 - rules.StopLossBps / 10000m)
                    ),
                    targetPrice = NullableDecimal(
                        rules.Adaptive is not null && position.StopPrice is not null
                            ? position.TargetPrice
                            : position.AverageCost * (1 + rules.TakeProfitBps / 10000m)
                    ),
                    trailingDistance = NullableDecimal(
                        rules.Adaptive is not null && position.StopPrice is not null
                            ? position.TrailingDistance
                            : position.PeakBid * rules.TrailingStopBps / 10000m
                    ),
                    trailingActive = position.ExitPlan?.TrailingActive ?? position.PeakBid > position.AverageCost,
                    timeExitAt = position.ExitPlan?.HardExitAt ?? position.OpenedAt.AddMinutes(rules.TimeExitMinutes),
                    exitPlan = position.ExitPlan is not { } plan
                        ? null
                        : new
                        {
                            plan.Version,
                            regime = JsonNamingPolicy.CamelCase.ConvertName(plan.Regime.ToString()),
                            plan.TimeframeSeconds,
                            plan.NextReviewAt,
                            plan.HardExitAt,
                            effectiveStop = Decimal(plan.EffectiveStop),
                            plan.TrailingActive,
                            plan.ReviewCount,
                            plan.Revision,
                            plan.LastReason,
                        },
                }),
            orders = ledger
                .Orders.OrderByDescending(order => order.CreatedAt)
                .Select(order => new
                {
                    id = order.Id.ToString("D"),
                    order.Symbol,
                    side = order.Side == TradeSide.Buy ? "buy" : "sell",
                    amount = Decimal(order.Amount),
                    limitPrice = Decimal(order.LimitPrice),
                    order.Status,
                    order.CreatedAt,
                    expiresAt = order.CreatedAt.AddMinutes(rules.OrderLifetimeMinutes),
                    order.BrokerOrderId,
                    order.Reason,
                    order.CancellationState,
                    order.LastInspectedAt,
                    order.LastInspectionError,
                    order.LastExecutionMessage,
                    order.LastExecutionAt,
                    originalAmount = order.OriginalAmount is { } originalAmount ? Decimal(originalAmount) : null,
                    remaining = Decimal(order.Remaining),
                }),
        };
    }
}
