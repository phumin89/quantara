using System.Globalization;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public static class PortfolioGoalAccounting
{
    public static PortfolioGoalCheckpoint Capture(string accountKey, CapitalReadResult evidence, DateTimeOffset now)
    {
        var snapshot = evidence.Snapshot;
        var observedAt = evidence.Markets.Select(book => book.ObservedAt).Append(snapshot.ObservedAt).Max();
        var reason =
            snapshot.AccountKey != accountKey ? "Account does not match the verified objective scope."
            : evidence.Evidence.ObservedAt != snapshot.ObservedAt
            || observedAt > now
            || now - snapshot.ObservedAt > TimeSpan.FromSeconds(PortfolioObjective.MaximumAgeSeconds)
            || evidence.Markets.Any(book =>
                now - book.ObservedAt > TimeSpan.FromSeconds(PortfolioObjective.MaximumAgeSeconds)
                && snapshot.Assets.Any(asset =>
                    asset.Symbol != "THB" && asset.Symbol == book.Symbol && asset.Quantity > 0
                )
            )
                ? "Account valuation is stale or from the future."
            : snapshot.HasUncertainOrders ? "Order outcomes must be reconciled for a coherent account valuation."
            : snapshot.Blocker;
        var cash = snapshot.Assets.SingleOrDefault(asset => asset.Symbol == "THB");
        if (
            reason is null
            && (cash is null || snapshot.Assets.Any(asset => asset.Quantity > 0 && asset.PriceThb is null))
        )
        {
            reason = "Cash or a held asset has no verified valuation.";
        }

        var value = reason is null
            ? cash?.Quantity
                + snapshot
                    .Assets.Where(asset => asset.Symbol != "THB")
                    .Sum(asset => asset.Quantity * (asset.PriceThb ?? 0))
            : null;
        if (reason is null && (value is null || value != snapshot.EquityThb))
        {
            reason = "Account totals do not match the same valuation snapshot.";
            value = null;
        }

        var deposits = CapitalFlowEvidence
            .Unique(evidence.Evidence.CashFlows, snapshot.ObservedAt)
            .Where(flow =>
                flow.Kind == CashFlowKind.Deposit
                && flow.At >= PortfolioObjective.StartsAt
                && flow.At <= snapshot.ObservedAt
            )
            .ToArray();
        decimal? depositTotal =
            snapshot.AccountKey == accountKey
            && evidence.Evidence.CashFlowCoverageFrom <= PortfolioObjective.StartsAt
            && deposits.All(flow => flow.ValueThb is not null)
                ? deposits.Sum(flow => flow.ValueThb ?? 0)
                : null;
        return new(accountKey, PortfolioObjective.Id, observedAt, value, depositTotal, null, reason);
    }

    public static object View(string accountKey, PortfolioGoalCheckpoint? checkpoint, DateTimeOffset now)
    {
        var cutoff = PortfolioObjective.Cutoff(now);
        var valid =
            checkpoint is not null
            && checkpoint.AccountKey == accountKey
            && checkpoint.ObjectiveId == PortfolioObjective.Id
            && checkpoint.ObservedAt <= cutoff
            && cutoff - checkpoint.ObservedAt <= TimeSpan.FromSeconds(PortfolioObjective.MaximumAgeSeconds)
            && checkpoint.PortfolioValueThb is not null
            && checkpoint.Reason is null;
        var value = valid ? checkpoint?.PortfolioValueThb : null;
        return new
        {
            id = PortfolioObjective.Id,
            targetThb = PortfolioObjective.TargetThb,
            deadlineExclusive = PortfolioObjective.DeadlineExclusive,
            includesDeposits = true,
            timeZone = "Asia/Bangkok",
            valuationProtocol = "verified-wallet-bid-v1",
            maximumAgeSeconds = PortfolioObjective.MaximumAgeSeconds,
            observedAt = checkpoint?.ObservedAt,
            portfolioValueThb = Format(value),
            remainingThb = value is { } known ? Format(decimal.Max(0, PortfolioObjective.TargetThb - known)) : null,
            depositsThb = valid ? Format(checkpoint?.DepositsThb) : null,
            tradingPnlThb = valid ? Format(checkpoint?.TradingPnlThb) : null,
            valuationStatus = valid ? "verified" : "unknown",
            reason = valid
                ? null
                : checkpoint?.Reason ?? "No complete fresh account valuation at the measurement cutoff.",
            accountingNote = "Reserved cash is included once. Deposits require complete confirmed flow coverage; full-account trading P&L remains Unknown until all costs and flows are reconciled. This objective never changes trading authority.",
            measurement = now >= PortfolioObjective.DeadlineExclusive ? "monthEnd" : "progress",
        };
    }

    private static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
}
