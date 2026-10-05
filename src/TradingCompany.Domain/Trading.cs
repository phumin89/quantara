namespace TradingCompany.Domain;

public readonly record struct Symbol
{
    public string Value { get; }
    public Symbol(string value) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Symbol is required.", nameof(value)); Value = value.Trim().ToUpperInvariant(); }
    public override string ToString() => Value;
}
public readonly record struct Percentage
{
    public decimal Value { get; }
    public Percentage(decimal value) { if (value is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(value)); Value = value; }
}
public enum OrderSide { Buy, Sell }
public sealed record TradeProposal(Guid Id, Symbol Symbol, OrderSide Side, decimal Quantity, decimal ExpectedPrice, Percentage Confidence, Guid CreatedByAgentId, bool OpensNewPosition = true);
public sealed record PortfolioSnapshot(decimal Equity, decimal Cash, decimal CurrentExposure, decimal DailyLoss, decimal Drawdown, int OpenPositions);
public sealed record RiskLimits(decimal MaximumOrderNotional, Percentage MaximumPortfolioExposure, decimal MaximumDailyLoss, Percentage MaximumDrawdown, int MaximumOpenPositions, bool KillSwitchEnabled);
public sealed record RiskViolation(string Code, string Message);
public sealed record RiskDecision(Guid ProposalId, bool Approved, IReadOnlyList<RiskViolation> Violations);
public sealed record HistoricalBar(DateOnly Date, Symbol Symbol, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
public sealed record BacktestTrade(DateOnly Date, OrderSide Side, decimal Quantity, decimal Price, decimal RealizedPnl);
public enum ForecastDirection { Bullish, Neutral, Bearish }
public sealed record DeterministicForecast(Symbol Symbol, DateOnly AsOf, decimal Score, ForecastDirection Direction, decimal Momentum20, decimal Momentum60, decimal Volatility);
public static class DeterministicForecaster
{
    public static DeterministicForecast Evaluate(IEnumerable<HistoricalBar> source, Symbol symbol)
    {
        var bars = source.Where(x => x.Symbol == symbol).OrderBy(x => x.Date).ToArray();
        if (bars.Length < 200) throw new ArgumentException("At least 200 completed bars are required.", nameof(source));
        var closes = bars.Select(x => x.Close).ToArray();
        var sma = (int n) => closes[^n..].Average();
        var m20 = closes[^1] / closes[^21] - 1m; var m60 = closes[^1] / closes[^61] - 1m;
        var mean = closes[^20..].Average(); var vol = (decimal)Math.Sqrt((double)closes[^20..].Select(x => Math.Pow((double)(x / mean - 1m), 2)).Average());
        var score = 50m + (sma(20) > sma(50) ? 15m : -15m) + (sma(50) > sma(200) ? 15m : -15m) + Math.Clamp(m20 * 100m, -10m, 10m) + Math.Clamp(m60 * 50m, -10m, 10m);
        score = Math.Clamp(score, 0m, 100m);
        var direction = score >= 70m ? ForecastDirection.Bullish : score <= 35m ? ForecastDirection.Bearish : ForecastDirection.Neutral;
        return new(symbol, bars[^1].Date, score, direction, m20, m60, vol);
    }
}
public sealed record PaperPosition(Symbol Symbol, decimal Quantity, decimal AveragePrice, decimal MarketPrice, decimal MarketValue, decimal UnrealizedPnl);
public enum PortfolioAction { Hold, Buy, Sell, Trim }
public static class TradeQuantity
{
    public static decimal FloorFor(Symbol symbol, decimal rawQuantity)
    {
        var step = symbol.Value.EndsWith("-THB", StringComparison.Ordinal) || symbol.Value.EndsWith("-USDT", StringComparison.Ordinal) ? .000001m : 1m;
        return rawQuantity <= 0m ? 0m : Math.Floor(rawQuantity / step) * step;
    }
}
public sealed class RiskEngine
{
    public RiskDecision Evaluate(TradeProposal proposal, PortfolioSnapshot portfolio, RiskLimits limits)
    {
        var violations = new List<RiskViolation>();
        if (limits.KillSwitchEnabled) violations.Add(new("KILL_SWITCH_ENABLED", "Trading is disabled."));
        var notional = proposal.Quantity * proposal.ExpectedPrice;
        if (proposal.Quantity <= 0 || proposal.ExpectedPrice <= 0) violations.Add(new("INVALID_ORDER", "Quantity and price must be positive."));
        if (proposal.Side == OrderSide.Buy && notional > limits.MaximumOrderNotional) violations.Add(new("MAX_ORDER_NOTIONAL_EXCEEDED", "Order notional exceeds the limit."));
        var resultingExposure = proposal.Side == OrderSide.Sell ? Math.Max(0m, portfolio.CurrentExposure - notional) : portfolio.CurrentExposure + notional;
        if (proposal.Side == OrderSide.Buy && (portfolio.Equity <= 0 || resultingExposure / portfolio.Equity > limits.MaximumPortfolioExposure.Value)) violations.Add(new("MAX_PORTFOLIO_EXPOSURE_EXCEEDED", "Resulting exposure exceeds the limit."));
        if (proposal.Side == OrderSide.Buy && portfolio.DailyLoss >= limits.MaximumDailyLoss) violations.Add(new("MAX_DAILY_LOSS_REACHED", "Daily loss limit is reached."));
        if (proposal.Side == OrderSide.Buy && portfolio.Equity > 0 && portfolio.Drawdown / portfolio.Equity >= limits.MaximumDrawdown.Value) violations.Add(new("MAX_DRAWDOWN_REACHED", "Drawdown limit is reached."));
        if (proposal.Side == OrderSide.Buy && proposal.OpensNewPosition && portfolio.OpenPositions >= limits.MaximumOpenPositions) violations.Add(new("MAX_OPEN_POSITIONS_REACHED", "Open-position limit is reached."));
        if (proposal.Side == OrderSide.Buy && notional > portfolio.Cash) violations.Add(new("INSUFFICIENT_CASH", "Available cash is insufficient."));
        return new(proposal.Id, violations.Count == 0, violations);
    }
}
