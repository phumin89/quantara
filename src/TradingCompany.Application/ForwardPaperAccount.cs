using TradingCompany.Domain;
using System.Text.Json.Serialization;

namespace TradingCompany.Application;

public sealed record ForwardPaperPolicy(decimal MaximumOrderWeight = .20m, decimal MaximumExposure = .80m,
    decimal MaximumDrawdown = .10m, int MaximumPositions = 4, bool KillSwitch = false);
public sealed record ForwardPaperIntent(Guid Id, string Symbol, OrderSide Side, decimal Quantity,
    DateTimeOffset DecisionAt, ForwardPaperPolicy Policy);
public sealed record ForwardPaperQuote(string Symbol, decimal NativePrice, string Currency, decimal ThbPerUnit,
    DateTimeOffset ObservedAt, DateTimeOffset AvailableAt, DateTimeOffset FxAt, string Source,
    decimal FeeThb = 0m, decimal SlippageThbPerUnit = 0m,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] decimal FeeRate = 0m,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] decimal? MaximumQuantity = null);
public sealed record ForwardPaperFill(Guid OrderId, ForwardPaperQuote Quote, string Status, string[] Violations);
public sealed record ForwardPaperEvent(DateTimeOffset At, string Kind, ForwardInvestmentCredit? Credit = null,
    ForwardPaperIntent? Order = null, ForwardPaperFill? Fill = null, ForwardPaperQuote? Mark = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? InitialCapital { get; init; }
}
public sealed record ForwardPaperAccount(int Version, Guid FundingAccountId, IReadOnlyList<ForwardPaperEvent> Events);
public sealed record ForwardPaperSnapshot(decimal TotalContributed, decimal Cash, decimal Equity, decimal TotalPnl,
    decimal RealizedPnl, decimal Drawdown, IReadOnlyList<PaperPosition> Positions,
    IReadOnlyList<ForwardPaperIntent> PendingOrders, IReadOnlyList<ForwardPaperFill> Receipts);

public static class ForwardPaperLedger
{
    public static ForwardPaperAccount Mark(ForwardPaperAccount account, ForwardPaperQuote quote, DateTimeOffset now)
    {
        var state = Replay(account);
        if (!state.Positions.ContainsKey(quote.Symbol) || !FreshMark(state, quote, now)) return account;
        return Append(account, new(now, "MARK", Mark: quote));
    }
    public static ForwardPaperAccount Open(Guid fundingId)
        => fundingId != Guid.Empty ? new(2, fundingId, []) : throw new ArgumentException("Funding identity is required.");

    public static ForwardPaperAccount InitializeCapital(ForwardPaperAccount account, decimal amount, DateTimeOffset now)
    {
        Validate(account);
        if (amount <= 0 || now == default)
            throw new InvalidDataException("Invalid initial paper capital or clock rollback.");
        var existing = account.Events.SingleOrDefault(e => e.InitialCapital is not null);
        if (existing is not null)
            // Two startup workers can capture time before acquiring the same disk lease.
            // Rechecking the identical saved seed is read-only, not a new cash event.
            return existing.InitialCapital == amount ? account : throw new InvalidDataException("Initial paper capital cannot be changed.");
        if (account.Events.Any(e => e.At > now)) throw new InvalidDataException("Cannot add capital before existing history.");
        if (account.Events.Any(e => e.Order is not null || e.Fill is not null || e.Mark is not null))
            throw new InvalidDataException("Initial capital must precede paper trading.");
        return Append(account, new(now, "INITIAL_CAPITAL") { InitialCapital = amount });
    }

    public static ForwardPaperAccount Fund(ForwardPaperAccount account, ForwardInvestmentCredit credit)
    {
        Validate(account);
        var existing = account.Events.FirstOrDefault(e => e.Credit?.SalaryDate == credit.SalaryDate)?.Credit;
        if (existing is not null)
            return existing == credit ? account : throw new InvalidDataException("Contribution identity reused with changed values.");
        return Append(account, new(credit.CreditedAt, "FUNDING", Credit: credit));
    }

    public static ForwardPaperAccount Submit(ForwardPaperAccount account, ForwardPaperIntent order, DateTimeOffset now)
    {
        Validate(account);
        var existing = account.Events.FirstOrDefault(e => e.Order?.Id == order.Id)?.Order;
        if (existing is not null)
            return existing == order ? account : throw new InvalidDataException("Order identity reused with changed intent.");
        return Append(account, new(now, "ORDER", Order: order));
    }

    public static ForwardPaperAccount Execute(ForwardPaperAccount account, Guid orderId, ForwardPaperQuote? quote, DateTimeOffset now)
    {
        var state = Replay(account);
        if (state.Receipts.Any(r => r.OrderId == orderId)) return account;
        if (!state.Orders.TryGetValue(orderId, out var order)) throw new InvalidDataException("Unknown paper order.");
        if (!Usable(quote, order, now) || (quote is not null && state.Observed.TryGetValue(quote.Symbol, out var last) && quote.ObservedAt < last)) return account;
        var violations = Decide(state, order, quote!, now);
        return Append(account, new(now, "EXECUTION", Fill: new(orderId, quote!,
            violations.Length == 0 ? "PAPER_FILLED" : "RISK_REJECTED", violations)));
    }

    public static ForwardPaperSnapshot Snapshot(ForwardPaperAccount account)
    {
        var state = Replay(account);
        return new(state.Contributed, state.Cash, state.Equity, state.Equity - state.Contributed,
            state.Realized, Math.Max(0m, state.Peak - state.Equity), state.Positions.Values.ToArray(),
            state.Orders.Values.Where(o => !state.Receipts.Any(r => r.OrderId == o.Id)).ToArray(), state.Receipts.ToArray());
    }

    public static void Validate(ForwardPaperAccount account) => Replay(account);

    private static ForwardPaperAccount Append(ForwardPaperAccount account, ForwardPaperEvent entry)
    {
        var next = account with { Events = account.Events.Append(entry).ToArray() };
        Validate(next);
        return next;
    }

    private sealed class State
    {
        public decimal Contributed, Cash, Realized, Peak;
        public decimal Exposure => Positions.Values.Sum(p => p.MarketValue);
        public decimal Equity => Cash + Exposure;
        public readonly Dictionary<Guid, ForwardPaperIntent> Orders = [];
        public readonly Dictionary<string, PaperPosition> Positions = new(StringComparer.Ordinal);
        public readonly Dictionary<string, DateTimeOffset> Observed = new(StringComparer.Ordinal);
        public readonly List<ForwardPaperFill> Receipts = [];
    }

    private static State Replay(ForwardPaperAccount account)
    {
        if (account is null || account.Version != 2 || account.FundingAccountId == Guid.Empty || account.Events is null)
            throw new InvalidDataException("Invalid forward paper account.");
        var state = new State();
        var salaryDates = new HashSet<DateOnly>();
        var initialized = false;
        var previous = DateTimeOffset.MinValue;
        foreach (var entry in account.Events)
        {
            if (entry is null || entry.At == default || entry.At < previous ||
                (entry.Credit is not null ? 1 : 0) + (entry.Order is not null ? 1 : 0) + (entry.Fill is not null ? 1 : 0) +
                (entry.Mark is not null ? 1 : 0) + (entry.InitialCapital is not null ? 1 : 0) != 1)
                throw new InvalidDataException("Invalid journal event or clock rollback.");
            previous = entry.At;
            switch (entry.Kind)
            {
                case "INITIAL_CAPITAL" when entry.InitialCapital is { } capital:
                    if (capital <= 0 || initialized || state.Orders.Count != 0)
                        throw new InvalidDataException("Invalid, duplicate or late initial paper capital.");
                    initialized = true;
                    state.Contributed += capital; state.Cash += capital; state.Peak += capital;
                    break;
                case "FUNDING" when entry.Credit is { } credit:
                    if (credit.Amount <= 0 || credit.CreditedAt != entry.At || credit.SalaryDate == default ||
                        credit.SalaryDate > ForwardFundingCalendar.BangkokDate(entry.At) || !salaryDates.Add(credit.SalaryDate))
                        throw new InvalidDataException("Invalid or duplicate contribution.");
                    state.Contributed += credit.Amount; state.Cash += credit.Amount; state.Peak += credit.Amount;
                    break;
                case "ORDER" when entry.Order is { } order:
                    ValidateOrder(order, entry.At);
                    if (!state.Orders.TryAdd(order.Id, order)) throw new InvalidDataException("Duplicate order in journal.");
                    break;
                case "EXECUTION" when entry.Fill is { } fill:
                    if (!state.Orders.TryGetValue(fill.OrderId, out var intent) || state.Receipts.Any(r => r.OrderId == fill.OrderId) ||
                        !Usable(fill.Quote, intent, entry.At) ||
                        (state.Observed.TryGetValue(intent.Symbol, out var observed) && fill.Quote.ObservedAt < observed))
                        throw new InvalidDataException("Unverifiable execution.");
                    var expected = Decide(state, intent, fill.Quote, entry.At);
                    if (fill.Status != (expected.Length == 0 ? "PAPER_FILLED" : "RISK_REJECTED") ||
                        fill.Violations is null || !expected.SequenceEqual(fill.Violations))
                        throw new InvalidDataException("Saved execution does not match deterministic risk validation.");
                    if (expected.Length == 0) ApplyFill(state, intent, fill.Quote);
                    state.Receipts.Add(fill);
                    break;
                case "MARK" when entry.Mark is { } mark:
                    if (!state.Positions.ContainsKey(mark.Symbol) || !FreshMark(state, mark, entry.At))
                        throw new InvalidDataException("Invalid valuation evidence.");
                    var position = state.Positions[mark.Symbol];
                    var market = mark.NativePrice * mark.ThbPerUnit;
                    state.Positions[mark.Symbol] = position with { MarketPrice = market, MarketValue = market * position.Quantity,
                        UnrealizedPnl = (market - position.AveragePrice) * position.Quantity };
                    state.Observed[mark.Symbol] = mark.ObservedAt;
                    break;
                default: throw new InvalidDataException("Unknown journal event.");
            }
            if (state.Cash < 0) throw new InvalidDataException("Paper cash cannot become negative.");
            state.Peak = Math.Max(state.Peak, state.Equity);
        }
        return state;
    }

    private static void ValidateOrder(ForwardPaperIntent order, DateTimeOffset now)
    {
        var p = order.Policy;
        if (order.Id == Guid.Empty || string.IsNullOrWhiteSpace(order.Symbol) || order.Symbol != order.Symbol.Trim().ToUpperInvariant() ||
            order.Quantity <= 0 || !Enum.IsDefined(order.Side) || order.DecisionAt == default || order.DecisionAt > now || p is null ||
            p.MaximumOrderWeight is <= 0 or > .20m || p.MaximumExposure is <= 0 or > .80m ||
            p.MaximumDrawdown is <= 0 or > .10m || p.MaximumPositions is <= 0 or > 4)
            throw new InvalidDataException("Invalid intent or policy exceeds server safety ceiling.");
        if (TradeQuantity.FloorFor(new(order.Symbol), order.Quantity) != order.Quantity)
            throw new InvalidDataException("Quantity is not executable for this instrument.");
    }

    private static bool Usable(ForwardPaperQuote? q, ForwardPaperIntent order, DateTimeOffset now)
        => q is not null && q.Symbol == order.Symbol && q.NativePrice > 0 && q.ThbPerUnit > 0 &&
            q.Currency is "USD" or "THB" && (q.Currency != "THB" || q.ThbPerUnit == 1m) &&
            q.FeeThb >= 0 && q.FeeRate is >= 0m and <= 1m && (q.MaximumQuantity is null or > 0m) &&
            q.SlippageThbPerUnit >= 0 && q.NativePrice * q.ThbPerUnit > q.SlippageThbPerUnit &&
            q.ObservedAt > order.DecisionAt && q.AvailableAt >= q.ObservedAt && q.AvailableAt <= now &&
            now - q.ObservedAt <= TimeSpan.FromMinutes(30) && q.FxAt != default && q.FxAt <= q.ObservedAt &&
            q.ObservedAt - q.FxAt <= TimeSpan.FromDays(7) && !string.IsNullOrWhiteSpace(q.Source);

    private static bool FreshMark(State state, ForwardPaperQuote quote, DateTimeOffset now)
        => Usable(quote, new(Guid.Empty, quote.Symbol, OrderSide.Buy, 1,
            state.Observed.GetValueOrDefault(quote.Symbol, DateTimeOffset.MinValue), new()), now);

    private static string[] Decide(State state, ForwardPaperIntent order, ForwardPaperQuote quote, DateTimeOffset now)
    {
        var symbol = new Symbol(order.Symbol);
        var violations = new List<string>();
        var price = quote.NativePrice * quote.ThbPerUnit + (order.Side == OrderSide.Buy ? 1m : -1m) * quote.SlippageThbPerUnit;
        var fee = quote.FeeThb + price * order.Quantity * quote.FeeRate;
        if (quote.MaximumQuantity is { } available && order.Quantity > available)
            violations.Add("INSUFFICIENT_QUOTED_DEPTH");
        if (order.Side == OrderSide.Sell && (!state.Positions.TryGetValue(order.Symbol, out var held) || held.Quantity < order.Quantity))
            violations.Add("INSUFFICIENT_POSITION");
        if (order.Side == OrderSide.Sell && price * order.Quantity < fee) violations.Add("FEES_EXCEED_PROCEEDS");
        if (order.Side == OrderSide.Buy)
        {
            if (state.Positions.Keys.Any(s => !state.Observed.TryGetValue(s, out var observed) || now - observed > TimeSpan.FromMinutes(30)))
                violations.Add("STALE_POSITION_VALUATION");
            if (state.Positions.ContainsKey(order.Symbol)) violations.Add("DUPLICATE_POSITION");
            if (MarketExposurePolicy.IsIncomeOverlay(symbol)) violations.Add("INCOME_OVERLAY_RESEARCH_ONLY");
            if (MarketExposurePolicy.HasEquivalentIndexExposure(symbol, state.Positions.Keys.Select(s => new Symbol(s))))
                violations.Add("OVERLAPPING_INDEX_EXPOSURE");
        }
        var policy = order.Policy;
        var risk = new RiskEngine().Evaluate(new(order.Id, symbol, order.Side, order.Quantity,
                price + (order.Side == OrderSide.Buy ? fee / order.Quantity : 0m), new(.5m), Guid.Empty),
            new(state.Equity, state.Cash, state.Exposure, 0m, Math.Max(0m, state.Peak - state.Equity), state.Positions.Count),
            new(state.Equity * policy.MaximumOrderWeight, new(policy.MaximumExposure), decimal.MaxValue,
                new(policy.MaximumDrawdown), policy.MaximumPositions, policy.KillSwitch));
        violations.AddRange(risk.Violations.Select(v => v.Code));
        return violations.ToArray();
    }

    private static void ApplyFill(State state, ForwardPaperIntent order, ForwardPaperQuote quote)
    {
        state.Observed[order.Symbol] = quote.ObservedAt;
        var market = quote.NativePrice * quote.ThbPerUnit;
        var price = market + (order.Side == OrderSide.Buy ? 1m : -1m) * quote.SlippageThbPerUnit;
        var fee = quote.FeeThb + price * order.Quantity * quote.FeeRate;
        if (order.Side == OrderSide.Buy)
        {
            var cost = price * order.Quantity + fee;
            state.Cash -= cost;
            state.Positions.Add(order.Symbol, new(new(order.Symbol), order.Quantity, cost / order.Quantity,
                market, market * order.Quantity, market * order.Quantity - cost));
        }
        else
        {
            var held = state.Positions[order.Symbol];
            var proceeds = price * order.Quantity - fee;
            state.Cash += proceeds;
            state.Realized += proceeds - held.AveragePrice * order.Quantity;
            var remaining = held.Quantity - order.Quantity;
            if (remaining == 0) state.Positions.Remove(order.Symbol);
            else state.Positions[order.Symbol] = held with { Quantity = remaining, MarketPrice = market,
                MarketValue = remaining * market, UnrealizedPnl = remaining * (market - held.AveragePrice) };
        }
    }
}
