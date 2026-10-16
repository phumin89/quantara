using System.Globalization;
using System.Text.Json;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingOverviewProjection(IAccountContext account, TradingControlOptions options, TimeProvider time)
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public string Serialize(TradingState state) =>
        JsonSerializer.Serialize(
            new
            {
                accountKey = account.AccountKey,
                mode = Mode(state.Mode),
                state.Rules,
                state.RulesHash,
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
                liveActivationAvailable = options.LiveActivationAvailable
                    && !string.IsNullOrWhiteSpace(options.OwnerToken),
                paper = Book(state.Paper, state.Rules.MaxDataAgeSeconds, time.GetUtcNow()),
                live = Book(state.Live, state.Rules.MaxDataAgeSeconds, time.GetUtcNow()),
                reports = Reports(state),
            },
            TradingOverviewProjection.jsonOptions
        );

    public static string SerializeReports(TradingState state) =>
        JsonSerializer.Serialize(new { reports = Reports(state) }, TradingOverviewProjection.jsonOptions);

    private static string Mode(TradingMode mode) => JsonNamingPolicy.CamelCase.ConvertName(mode.ToString());

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

    private static object Book(TradingLedger ledger, int freshnessSeconds, DateTimeOffset now)
    {
        var mark = ledger.Equity.OrderBy(item => item.At).LastOrDefault();
        decimal? equity =
            mark is { EquityThb: not null }
            && mark.At <= now
            && now - mark.At <= TimeSpan.FromSeconds(freshnessSeconds)
            && mark.FundingThb == ledger.FundingThb
                ? mark.EquityThb
                : null;
        var sells = ledger.Fills.Where(fill => fill.Side == TradeSide.Sell).ToArray();
        var known = ledger.FundingThb > 0;
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
                    order.BrokerOrderId,
                    order.Reason,
                    remaining = Decimal(order.Remaining),
                }),
        };
    }
}
