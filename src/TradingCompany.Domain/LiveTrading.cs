namespace TradingCompany.Domain;

public enum TradingMode { Sandbox, Live }
public readonly record struct LiveAccountId(Guid Value);
public enum LiveOrderState { Proposed, Rejected, Reserved, Dispatching, Unknown, Acknowledged, Open, PartiallyFilled, Filled, Cancelled }

public sealed record LiveMandate(LiveAccountId AccountId, long Version, string CredentialFingerprint,
    decimal CapitalAllocation, decimal MaximumOrderNotional, decimal MaximumDailyTurnover,
    decimal MaximumDailyLoss, decimal MaximumDrawdown, decimal MaximumPortfolioExposure,
    int MaximumOpenPositions, decimal MaximumSlippage, decimal FeeReserveRate,
    IReadOnlyList<string> AllowedSymbols, DateTimeOffset? ExpiresAt);
public sealed record LiveIntent(LiveAccountId AccountId, Guid DecisionId, Guid Id, TradingMode Mode,
    string Symbol, OrderSide Side, decimal Quantity, decimal LimitPrice, long MandateVersion,
    long SnapshotVersion, string EvidenceHash)
{
    // Opt-in maximum cash debit INCLUDING cash fees. Null preserves exact-quantity orders.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? BuyBudget { get; init; }
}
public sealed record LivePosition(string Symbol, decimal Quantity, decimal ReservedQuantity, decimal MarketPrice);
public sealed record LiveQuote(string Symbol, decimal Bid, decimal Ask, DateTimeOffset ObservedAt);

/// <summary>CashBalance includes reserved cash. Positions and equity contain only owner-allocated capital.</summary>
public sealed record LiveSnapshot(LiveAccountId AccountId, long Version, DateTimeOffset ObservedAt,
    decimal CashBalance, decimal CashReserved, decimal PendingBuyNotional, IReadOnlyList<LivePosition> Positions,
    decimal Equity, decimal DailyLoss, decimal Drawdown, decimal DailyTurnover, bool Reconciled,
    IReadOnlyList<LiveQuote> Quotes)
{
    public decimal PendingSellNotional { get; init; }
    public IReadOnlyList<string> PendingBuySymbols { get; init; } = [];
}
public sealed record LiveGateState(bool Armed, bool Stopped, string CredentialFingerprint, bool IsolationVerified, long ArmedMandateVersion);
public sealed record LiveGateDecision(bool Allowed, IReadOnlyList<string> Violations);

public static class LiveRiskPolicy
{
    private static readonly HashSet<string> SupportedSymbols = ["BTC-THB", "ETH-THB", "SOL-THB"];

    public static LiveGateDecision Evaluate(LiveIntent intent, LiveSnapshot snapshot, LiveMandate? mandate,
        LiveGateState gate, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (intent.Mode != TradingMode.Live) errors.Add("MODE_MISMATCH");
        if (!gate.Armed) errors.Add("NOT_ARMED");
        if (gate.Stopped) errors.Add("STOPPED");
        if (!gate.IsolationVerified) errors.Add("ISOLATION_REQUIRED");
        if (!snapshot.Reconciled) errors.Add("RECONCILIATION_REQUIRED");
        if (!Fresh(snapshot.ObservedAt, now)) errors.Add("STALE_SNAPSHOT");
        if (mandate is null || !IsValidMandate(mandate))
        {
            errors.Add("INVALID_MANDATE");
            return new(false, errors);
        }
        if (intent.AccountId.Value == Guid.Empty || intent.AccountId != mandate.AccountId || intent.AccountId != snapshot.AccountId)
            errors.Add("ACCOUNT_MISMATCH");
        if (intent.MandateVersion != mandate.Version || intent.SnapshotVersion != snapshot.Version || gate.ArmedMandateVersion != mandate.Version)
            errors.Add("STALE_APPROVAL");
        if (string.IsNullOrWhiteSpace(gate.CredentialFingerprint) || gate.CredentialFingerprint != mandate.CredentialFingerprint)
            errors.Add("ACCOUNT_BINDING_CHANGED");
        if (mandate.ExpiresAt is { } expiry && expiry <= now) errors.Add("MANDATE_EXPIRED");
        if (!mandate.AllowedSymbols.Contains(intent.Symbol, StringComparer.Ordinal) || !SupportedSymbols.Contains(intent.Symbol))
            errors.Add("SYMBOL_NOT_ALLOWED");
        if (intent.Id == Guid.Empty || intent.DecisionId == Guid.Empty || string.IsNullOrWhiteSpace(intent.EvidenceHash) ||
            intent.Quantity <= 0 || intent.LimitPrice <= 0 || !Enum.IsDefined(intent.Side))
            errors.Add("INVALID_INTENT");
        try
        {
            if (intent.BuyBudget is { } budget &&
                (intent.Side != OrderSide.Buy || budget <= 0 || budget != intent.Quantity * intent.LimitPrice))
                errors.Add("INVALID_INTENT");
            if (snapshot.Version <= 0 || snapshot.CashBalance < 0 || snapshot.CashReserved < 0 || snapshot.CashReserved > snapshot.CashBalance ||
                snapshot.PendingBuyNotional < 0 || snapshot.PendingSellNotional < 0 || snapshot.DailyLoss < 0 || snapshot.Drawdown < 0 || snapshot.DailyTurnover < 0 || snapshot.Equity <= 0 ||
                snapshot.PendingBuySymbols is null || snapshot.PendingBuySymbols.Any(s => !SupportedSymbols.Contains(s)) ||
                snapshot.Positions is null || snapshot.Quotes is null ||
                snapshot.Positions.Any(p => p.Quantity <= 0 || p.ReservedQuantity < 0 || p.ReservedQuantity > p.Quantity || p.MarketPrice <= 0 || !SupportedSymbols.Contains(p.Symbol)) ||
                snapshot.Positions.Select(p => p.Symbol).Distinct().Count() != snapshot.Positions.Count ||
                snapshot.Equity != snapshot.CashBalance + snapshot.Positions.Sum(p => p.Quantity * p.MarketPrice))
            {
                errors.Add("INVALID_SNAPSHOT");
                return new(false, errors.Distinct().ToArray());
            }
            var quoteMatches = snapshot.Quotes.Where(q => q.Symbol == intent.Symbol).ToArray();
            if (quoteMatches.Length != 1 || quoteMatches[0].Bid <= 0 || quoteMatches[0].Ask < quoteMatches[0].Bid || !Fresh(quoteMatches[0].ObservedAt, now))
                errors.Add("QUOTE_UNAVAILABLE");
            else if ((intent.Side == OrderSide.Buy && intent.LimitPrice > quoteMatches[0].Ask * (1 + mandate.MaximumSlippage)) ||
                (intent.Side == OrderSide.Sell && intent.LimitPrice < quoteMatches[0].Bid * (1 - mandate.MaximumSlippage)))
                errors.Add("PRICE_BOUND_EXCEEDED");

            if (errors.Contains("INVALID_INTENT")) return new(false, errors.Distinct().ToArray());
            var notional = intent.Quantity * intent.LimitPrice;
            var withFee = notional * (1 + mandate.FeeReserveRate);
            var exposure = snapshot.Positions.Sum(p => p.Quantity * p.MarketPrice);
            var available = snapshot.CashBalance - snapshot.CashReserved;
            if (withFee > mandate.MaximumOrderNotional) errors.Add("MAX_ORDER_NOTIONAL_EXCEEDED");
            if (snapshot.DailyTurnover + (snapshot.PendingBuyNotional + snapshot.PendingSellNotional) * (1 + mandate.FeeReserveRate) + withFee > mandate.MaximumDailyTurnover)
                errors.Add("DAILY_TURNOVER_EXCEEDED");
            if (intent.Side == OrderSide.Buy && exposure + snapshot.CashReserved + withFee > mandate.CapitalAllocation)
                errors.Add("CAPITAL_ALLOCATION_EXCEEDED");
            var position = snapshot.Positions.SingleOrDefault(p => p.Symbol == intent.Symbol);
            if (intent.Side == OrderSide.Sell && (position is null || intent.Quantity > position.Quantity - position.ReservedQuantity))
                errors.Add("INSUFFICIENT_OWNED_QUANTITY");

            var occupiedSymbols = snapshot.Positions.Select(p => p.Symbol).Union(snapshot.PendingBuySymbols, StringComparer.Ordinal).ToArray();
            var proposal = new TradeProposal(intent.Id, new Symbol(intent.Symbol), intent.Side, intent.Quantity,
                intent.LimitPrice * (1 + mandate.FeeReserveRate), new Percentage(0), Guid.Empty, !occupiedSymbols.Contains(intent.Symbol));
            var risk = new RiskEngine().Evaluate(proposal,
                new(Math.Min(snapshot.Equity, mandate.CapitalAllocation), available, exposure + snapshot.PendingBuyNotional,
                    snapshot.DailyLoss, snapshot.Drawdown, occupiedSymbols.Length),
                new(mandate.MaximumOrderNotional, new(mandate.MaximumPortfolioExposure), mandate.MaximumDailyLoss,
                    new(mandate.MaximumDrawdown), mandate.MaximumOpenPositions, gate.Stopped));
            errors.AddRange(risk.Violations.Select(v => v.Code));
        }
        catch (OverflowException) { errors.Add("INVALID_INTENT"); }
        return new(errors.Count == 0, errors.Distinct().ToArray());
    }

    private static bool Fresh(DateTimeOffset observed, DateTimeOffset now) => observed <= now && now - observed <= TimeSpan.FromSeconds(30);

    public static bool IsValidMandate(LiveMandate mandate) => mandate.AccountId.Value != Guid.Empty && mandate.Version > 0 &&
        !string.IsNullOrWhiteSpace(mandate.CredentialFingerprint) && mandate.CapitalAllocation > 0 && mandate.MaximumOrderNotional > 0 &&
        mandate.MaximumDailyTurnover > 0 && mandate.MaximumDailyLoss > 0 && mandate.MaximumDrawdown is > 0 and <= 1 &&
        mandate.MaximumPortfolioExposure is > 0 and <= 1 && mandate.MaximumOpenPositions > 0 &&
        mandate.MaximumSlippage is >= 0 and <= .02m && mandate.FeeReserveRate is > 0 and <= .1m &&
        mandate.AllowedSymbols is { Count: > 0 } && mandate.AllowedSymbols.All(SupportedSymbols.Contains) &&
        mandate.AllowedSymbols.Distinct().Count() == mandate.AllowedSymbols.Count;
}
