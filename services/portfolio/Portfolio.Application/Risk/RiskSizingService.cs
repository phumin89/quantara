using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class RiskSizingService(
    IRiskStore store,
    RiskCapitalReader capitalReader,
    IAccountContext account,
    TimeProvider time,
    IReadOnlyTradingStore trading
) : IRiskSizingService
{
    public async Task<RiskSizingSnapshot?> Size(
        Guid orderId,
        string symbol,
        TradeSide side,
        decimal requestedAmount,
        decimal limit,
        RiskSignal? signal,
        CancellationToken cancellationToken
    )
    {
        var policy = await store.ReadPolicy(cancellationToken);
        if (policy.Policy.Profile == RiskProfile.Disabled)
        {
            return null;
        }

        return await this.Evaluate(
            orderId,
            symbol,
            side,
            requestedAmount,
            limit,
            signal,
            policy,
            false,
            cancellationToken
        );
    }

    public async Task<CapitalSnapshot?> Revalidate(OrderProposal proposal, CancellationToken cancellationToken)
    {
        var policy = await store.ReadPolicy(cancellationToken);
        if (proposal.Sizing is null)
        {
            if (policy.Policy.Profile != RiskProfile.Disabled)
            {
                throw new OrderValidationException("Risk policy changed. Request and review a freshly sized proposal.");
            }

            return null;
        }

        if (
            proposal.Sizing.AccountKey != account.AccountKey
            || proposal.Sizing.PolicyHash != policy.PolicyHash
            || proposal.Sizing.Decision.Amount != proposal.Amount
        )
        {
            throw new OrderValidationException(
                "Account or risk policy changed. Request a fresh proposal; the reviewed amount was not modified."
            );
        }

        var current = await this.Evaluate(
            proposal.Id,
            proposal.Symbol,
            proposal.Side,
            proposal.Sizing.RequestedAmount,
            proposal.LimitPrice,
            proposal.Sizing.Signal,
            policy,
            true,
            cancellationToken
        );
        if (current.Decision.Amount < proposal.Amount)
        {
            throw new OrderValidationException(
                "Capital, exposure or liquidity no longer supports this exact amount. Request a fresh proposal; no order was sent."
            );
        }

        return current.Capital;
    }

    private async Task<RiskSizingSnapshot> Evaluate(
        Guid orderId,
        string symbol,
        TradeSide side,
        decimal requestedAmount,
        decimal limit,
        RiskSignal? signal,
        RiskPolicyState policy,
        bool excludeSelf,
        CancellationToken cancellationToken
    )
    {
        if ((await trading.Read(cancellationToken)).Mode != TradingMode.ManualReview)
        {
            throw new OrderValidationException(
                "Adaptive proposals require ManualReview mode. This profile does not authorize Auto Trading."
            );
        }

        if (signal is null)
        {
            throw new OrderValidationException(
                "Adaptive sizing requires structured signal and stop evidence. AI amounts cannot substitute for risk evidence."
            );
        }

        var capital = await capitalReader.Read(symbol, excludeSelf ? orderId : null, policy.Policy, cancellationToken);
        await store.Observe(
            capital.Snapshot,
            capital.Evidence.CashFlows,
            capital.Evidence.CashFlowCoverageFrom,
            cancellationToken
        );
        var book =
            capital.Markets.SingleOrDefault(item => item.Symbol == symbol)
            ?? throw new OrderValidationException("A fresh risk market book is unavailable.");
        var now = time.GetUtcNow();
        var bars = book
            .Bars.Where(bar => bar.Timestamp + 300 <= now.ToUnixTimeSeconds())
            .OrderBy(bar => bar.Timestamp)
            .TakeLast(21)
            .ToArray();
        var coherent =
            bars.Length == 21
            && bars.Zip(bars.Skip(1)).All(pair => pair.Second.Timestamp - pair.First.Timestamp == 300)
            && bars.All(bar => bar.Low > 0 && bar.High >= bar.Low && bar.Close >= bar.Low && bar.Close <= bar.High)
            && bars[^1].Timestamp == now.ToUnixTimeSeconds() / 300 * 300 - 300;
        var anchor =
            coherent && side == TradeSide.Buy
                ? bars.TakeLast(20)
                    .LastOrDefault(bar =>
                        bar.Low == signal.StopPrice && (signal.StopCandle is null || signal.StopCandle == bar.Timestamp)
                    )
                : null;
        var validatedSignal = side == TradeSide.Buy ? signal with { StopCandle = anchor?.Timestamp } : signal;
        var atr = coherent
            ? bars.Zip(bars.Skip(1))
                .Average(pair =>
                    decimal.Max(
                        pair.Second.High - pair.Second.Low,
                        decimal.Max(
                            decimal.Abs(pair.Second.High - pair.First.Close),
                            decimal.Abs(pair.Second.Low - pair.First.Close)
                        )
                    )
                )
            : 0;
        var evidence = new RiskMarketEvidence(
            symbol,
            book.ObservedAt,
            book.Bid,
            book.Ask,
            book.AskQuantity * book.Ask,
            book.BidQuantity,
            atr,
            book.MinimumNotional,
            book.PriceStep,
            book.PriceScale,
            side == TradeSide.Buy ? book.QuoteScale : book.BaseScale,
            book.Enabled,
            anchor is not null
        );
        var decision = AdaptiveRiskEngine.Evaluate(
            policy.Policy,
            capital.Snapshot,
            evidence,
            side,
            limit,
            validatedSignal,
            now
        );
        if (
            side == TradeSide.Buy
            && (
                capital.Evidence.OpenOrders.Any(order => order.Symbol == symbol)
                || capital.Snapshot.Assets.Any(asset => asset.Symbol == symbol && asset.CommittedBuyThb > 0)
            )
        )
        {
            decision = new(
                false,
                "duplicateOrder",
                null,
                null,
                null,
                null,
                null,
                null,
                "An existing broker order for this symbol must be reconciled before another entry."
            );
        }

        var snapshot = new RiskSizingSnapshot(
            Guid.NewGuid(),
            account.AccountKey,
            policy.PolicyHash,
            policy.Policy,
            capital.Snapshot,
            validatedSignal,
            evidence,
            decision,
            requestedAmount
        );
        await store.RecordSizing(orderId, snapshot, cancellationToken);
        if (!decision.Accepted)
        {
            throw new OrderValidationException(decision.Explanation);
        }

        return snapshot;
    }
}
