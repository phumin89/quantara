using System.Security.Cryptography;
using System.Text.Json;
using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record LiveTradePlanRequest(LiveAccountId AccountId, Guid Id, Guid DecisionId,
    long MandateVersion, long SnapshotVersion, string Symbol, string EvidenceHash, AiCompanyTradePlan Plan);
public sealed record LiveTradePlanReceipt(Guid Id, bool Accepted, IReadOnlyList<string> Violations);
public sealed record LiveTradePlanView(Guid Id, string Symbol, AiCompanyTradePlan Plan,
    string Status, decimal AcquiredQuantity, decimal SoldQuantity);

// Plans are immutable audit records. Actual order/fill records determine their progress,
// so restoring a process cannot reset a plan or invent a completed trade.
public static class LiveTradePlans
{
    public const string Created = "TradePlanCreated";
    public const string Invalidated = "TradePlanInvalidated";
    public static Guid PlanId(Guid decisionId, string symbol) => new(SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes($"{decisionId:N}:plan:{symbol}"))[..16]);
    public static Guid IntentId(Guid planId, OrderSide side) => new(SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes($"{planId:N}:{side}"))[..16]);

    public static IReadOnlyList<LiveTradePlanRequest> Read(LiveAccountState account) => account.Audit
        .Where(a => a.Kind == Created).Select(a => JsonSerializer.Deserialize<LiveTradePlanRequest>(a.Detail)
            ?? throw new InvalidDataException("Invalid saved trade plan.")).ToArray();

    public static IReadOnlyList<LiveTradePlanView> Views(LiveAccountState account, DateTimeOffset now,
        IReadOnlyList<LiveOrderSizing>? sizing = null) =>
        Read(account).Select(p => View(account, p, now, sizing?.SingleOrDefault(s => s.Symbol == p.Symbol))).ToArray();

    public static LiveTradePlanView View(LiveAccountState account, LiveTradePlanRequest p, DateTimeOffset now, LiveOrderSizing? sizing = null)
    {
        var entry = account.Orders.SingleOrDefault(o => o.Intent.Id == IntentId(p.Id, OrderSide.Buy));
        var exit = account.Orders.SingleOrDefault(o => o.Intent.Id == IntentId(p.Id, OrderSide.Sell));
        var acquired = entry?.FilledQuantity ?? 0;
        var sold = exit?.FilledQuantity ?? 0;
        var status = entry is null ? (p.Plan.ExpiresAt <= now ? "EXPIRED" : "WAITING_ENTRY") :
            !Terminal(entry.State) ? (p.Plan.ExpiresAt <= now || entry.State == LiveOrderState.Unknown ? "REVIEW_REQUIRED" : "ENTRY_PENDING") :
            acquired <= 0 ? "ENTRY_REJECTED" : exit is null ? "WATCHING_EXIT" :
            !Terminal(exit.State) ? (exit.State == LiveOrderState.Unknown ? "REVIEW_REQUIRED" : "EXIT_PENDING") :
            exit.State == LiveOrderState.Filled ? "CLOSED" : "REVIEW_REQUIRED";
        if (entry is null && (account.Mandate.Version != p.MandateVersion ||
            account.Audit.Any(e => e.Kind == Invalidated && e.IntentId == p.Id))) status = "INVALIDATED";
        else if (status is "WAITING_ENTRY" or "WATCHING_EXIT" &&
            (account.Mandate.Version != p.MandateVersion || account.Gate.Stopped)) status = "REVIEW_REQUIRED";
        if (status == "WATCHING_EXIT" && sizing is { BaseStep: > 0 } &&
            account.Snapshot.Quotes.SingleOrDefault(q => q.Symbol == p.Symbol) is { Bid: > 0 } quote &&
            decimal.Floor(acquired / sizing.BaseStep) * sizing.BaseStep * ExitLimitPrice(p.Plan, quote, sizing.PriceStep,
                account.Mandate.MaximumSlippage) < sizing.MinimumQuoteSize) status = "REVIEW_REQUIRED";
        return new(p.Id, p.Symbol, p.Plan, status, acquired, sold);
    }
    public static decimal ExitLimitPrice(AiCompanyTradePlan plan, LiveQuote quote, decimal priceStep, decimal maximumSlippage)
    {
        var floor = quote.Bid * (1 - maximumSlippage);
        if (quote.Bid >= plan.TakeProfitPrice) floor = Math.Max(floor, plan.TakeProfitPrice);
        return decimal.Ceiling(floor / priceStep) * priceStep;
    }
    public static bool Active(LiveTradePlanView p) => p.Status is "WAITING_ENTRY" or "ENTRY_PENDING" or "WATCHING_EXIT" or "EXIT_PENDING" or "REVIEW_REQUIRED";
    public static bool Terminal(LiveOrderState state) => state is LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Rejected;

    public static IReadOnlyList<string> Validate(LiveAccountState a, LiveTradePlanRequest p, LiveOrderSizing s, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (p.AccountId != a.AccountId || p.Id == Guid.Empty || p.DecisionId == Guid.Empty ||
            p.EvidenceHash is not { Length: 64 } || p.EvidenceHash.Any(c => !char.IsAsciiHexDigit(c))) errors.Add("INVALID_PLAN_IDENTITY");
        if (p.Plan is not { } plan || plan.EntryPrice <= 0 || plan.Quantity <= 0 || plan.StopLossPrice <= 0 ||
            plan.StopLossPrice >= plan.EntryPrice || plan.TakeProfitPrice <= plan.EntryPrice ||
            plan.ExpiresAt <= now.AddMinutes(1) || plan.ExpiresAt > now.AddDays(7)) return [.. errors, "INVALID_PLAN"];
        if (s.Symbol != p.Symbol || s.PriceStep <= 0 || s.BaseStep <= 0 || s.QuoteStep <= 0 || s.MinimumQuoteSize <= 0 ||
            plan.EntryPrice % s.PriceStep != 0 || plan.TakeProfitPrice % s.PriceStep != 0 || plan.StopLossPrice % s.PriceStep != 0 ||
            plan.Quantity % s.BaseStep != 0) return [.. errors, "PLAN_SIZING_INVALID"];
        var fee = a.Mandate.FeeReserveRate;
        var budget = plan.Quantity * plan.EntryPrice;
        var wireQuote = decimal.Floor(budget / (1 + fee) / s.QuoteStep) * s.QuoteStep;
        var net = decimal.Floor(wireQuote / plan.EntryPrice / s.BaseStep) * s.BaseStep;
        if (wireQuote < s.MinimumQuoteSize ||
            net * plan.StopLossPrice * (1 - a.Mandate.MaximumSlippage) < s.MinimumQuoteSize) errors.Add("PLAN_EXIT_BELOW_MINIMUM");
        if (plan.TakeProfitPrice * (1 - fee) <= plan.EntryPrice * (1 + fee)) errors.Add("PLAN_TARGET_BELOW_COSTS");
        var intent = new LiveIntent(p.AccountId, p.DecisionId, IntentId(p.Id, OrderSide.Buy), TradingMode.Live,
            p.Symbol, OrderSide.Buy, plan.Quantity, plan.EntryPrice, p.MandateVersion, p.SnapshotVersion, p.EvidenceHash);
        var active = Views(a, now).Where(Active).ToArray();
        if (active.Any(v => v.Symbol == p.Symbol)) errors.Add("PLAN_ALREADY_ACTIVE");
        var waiting = active.Where(v => v.AcquiredQuantity == 0 && v.Status is "WAITING_ENTRY" or "REVIEW_REQUIRED").ToArray();
        var reserved = waiting.Sum(v => v.Plan.Quantity * v.Plan.EntryPrice * (1 + fee));
        var view = a.Snapshot with { CashReserved = a.Snapshot.CashReserved + reserved,
            PendingBuyNotional = a.Snapshot.PendingBuyNotional + reserved,
            PendingBuySymbols = a.Snapshot.PendingBuySymbols.Concat(waiting.Select(v => v.Symbol)).Distinct().ToArray() };
        // Entry is a ceiling, not the send price. Registration itself never sends.
        var gate = LiveRiskPolicy.Evaluate(intent, view, a.Mandate, a.Gate, now);
        errors.AddRange(gate.Violations.Where(v => v != "PRICE_BOUND_EXCEEDED"));
        if (a.PerformanceBaseline?.BangkokDate != DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime)) errors.Add("DAILY_BASELINE_REQUIRED");
        if (a.Orders.Any(o => !Terminal(o.State))) errors.Add("UNRESOLVED_ORDER");
        if (reserved + budget * (1 + fee) > a.Snapshot.CashBalance - a.Snapshot.CashReserved) errors.Add("PLAN_CASH_OVERCOMMITTED");
        var occupied = a.Snapshot.Positions.Select(x => x.Symbol).Concat(waiting.Select(x => x.Symbol)).Append(p.Symbol).Distinct().Count();
        if (occupied > a.Mandate.MaximumOpenPositions) errors.Add("OPEN_POSITION_LIMIT");
        // Keep room for the planned sell as well as the buy under the unchanged daily cap.
        var roundTrips = waiting.Sum(v => v.Plan.Quantity * (v.Plan.EntryPrice + v.Plan.TakeProfitPrice) * (1 + fee));
        roundTrips += active.Where(v => v.AcquiredQuantity > v.SoldQuantity).Sum(v =>
            (v.AcquiredQuantity - v.SoldQuantity) * v.Plan.TakeProfitPrice * (1 + fee));
        if (roundTrips + plan.Quantity * (plan.EntryPrice + plan.TakeProfitPrice) * (1 + fee) + a.Snapshot.DailyTurnover > a.Mandate.MaximumDailyTurnover)
            errors.Add("PLAN_ROUND_TRIP_TURNOVER_EXCEEDED");
        if (plan.Quantity * plan.TakeProfitPrice * (1 + fee) > a.Mandate.MaximumOrderNotional) errors.Add("PLAN_EXIT_ORDER_LIMIT");
        return errors.Distinct().ToArray();
    }

    public static IReadOnlyList<string> IntentViolations(LiveAccountState a, LiveIntent intent, DateTimeOffset now)
    {
        if (intent.TradePlanId is not { } id)
        {
            if (intent.Side != OrderSide.Sell) return [];
            var protectedQuantity = Views(a, now).Where(p => p.Symbol == intent.Symbol && Active(p)).Sum(p => p.AcquiredQuantity - p.SoldQuantity);
            var held = a.Snapshot.Positions.SingleOrDefault(p => p.Symbol == intent.Symbol);
            return held is not null && protectedQuantity > 0 && intent.Quantity > held.Quantity - protectedQuantity ? ["PLAN_QUANTITY_RESERVED"] : [];
        }
        var p = Read(a).SingleOrDefault(p => p.Id == id);
        if (p is null || p.AccountId != intent.AccountId || p.DecisionId != intent.DecisionId || p.Symbol != intent.Symbol ||
            p.MandateVersion != intent.MandateVersion || p.EvidenceHash != intent.EvidenceHash || intent.Id != IntentId(id, intent.Side)) return ["PLAN_BINDING_INVALID"];
        var quote = a.Snapshot.Quotes.SingleOrDefault(q => q.Symbol == p.Symbol);
        if (quote is null) return ["QUOTE_UNAVAILABLE"];
        if (intent.Side == OrderSide.Buy)
            return a.Audit.Any(e => e.Kind == Invalidated && e.IntentId == id) || now >= p.Plan.ExpiresAt || quote.Ask > p.Plan.EntryPrice || quote.Bid <= p.Plan.StopLossPrice ||
                intent.LimitPrice > p.Plan.EntryPrice || intent.Quantity > p.Plan.Quantity || intent.BuyBudget is null
                ? ["PLAN_ENTRY_NOT_TRIGGERED"] : [];
        var entry = a.Orders.SingleOrDefault(o => o.Intent.Id == IntentId(id, OrderSide.Buy));
        if (entry is null || !Terminal(entry.State) || entry.FilledQuantity <= 0 || intent.Quantity > entry.FilledQuantity ||
            !(quote.Bid >= p.Plan.TakeProfitPrice || quote.Bid <= p.Plan.StopLossPrice || now >= p.Plan.ExpiresAt)) return ["PLAN_EXIT_NOT_TRIGGERED"];
        return [];
    }
}

public sealed partial class LiveAccountTransaction
{
    public void InvalidateTradePlan(Guid id, DateTimeOffset now)
    {
        var p = LiveTradePlans.Read(Account).SingleOrDefault(p => p.Id == id) ?? throw new InvalidDataException("Unknown trade plan.");
        if (Account.Audit.Any(a => a.Kind == LiveTradePlans.Invalidated && a.IntentId == id)) return;
        var quote = Account.Snapshot.Quotes.SingleOrDefault(q => q.Symbol == p.Symbol);
        if (Account.Orders.Any(o => o.Intent.Id == LiveTradePlans.IntentId(id, OrderSide.Buy)) || quote is null ||
            quote.Bid <= 0 || quote.Ask < quote.Bid || quote.ObservedAt > now || now - quote.ObservedAt > TimeSpan.FromSeconds(30) ||
            quote.Bid > p.Plan.StopLossPrice) throw new InvalidOperationException("Plan invalidation condition is unavailable.");
        Audit(now, LiveTradePlans.Invalidated, id, "STOP_BEFORE_ENTRY");
    }

    public LiveTradePlanReceipt RecordTradePlan(LiveTradePlanRequest plan, LiveOrderSizing sizing, DateTimeOffset now)
    {
        var previous = LiveTradePlans.Read(Account).SingleOrDefault(p => p.Id == plan.Id);
        if (previous is not null)
        {
            if (previous != plan) throw new InvalidDataException("Trade plan identity was reused.");
            return new(plan.Id, true, []);
        }
        var errors = LiveTradePlans.Validate(Account, plan, sizing, now);
        if (errors.Count > 0) return new(plan.Id, false, errors);
        Audit(now, LiveTradePlans.Created, plan.Id, JsonSerializer.Serialize(plan));
        return new(plan.Id, true, []);
    }
}
