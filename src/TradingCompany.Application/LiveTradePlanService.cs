using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed class LiveTradePlanService(ILiveTradingStore store, LiveExecutionService execution,
    TimeProvider clock, IReadOnlyList<LiveOrderSizing> sizing)
{
    private readonly SemaphoreSlim tick = new(1, 1);
    public Task<LiveTradePlanReceipt> RegisterAsync(LiveTradePlanRequest plan, CancellationToken token = default)
        => store.WithAccountLockAsync(plan.AccountId, async (tx, ct) =>
        {
            var rules = sizing.SingleOrDefault(s => s.Symbol == plan.Symbol);
            if (rules is null) return new(plan.Id, false, ["SYMBOL_NOT_ALLOWED"]);
            var result = tx.RecordTradePlan(plan, rules, clock.GetUtcNow());
            if (result.Accepted) await tx.CommitAsync(ct);
            return result;
        }, token);

    public async Task TickAsync(LiveAccountId id, CancellationToken token = default)
    {
        if (!await tick.WaitAsync(0, token)) return;
        try
        {
            var account = await store.ReadAccountAsync(id, token);
            // This monitor consumes existing owner authority; it cannot restore activation.
            if (!account.Gate.Armed || account.Gate.Stopped || !account.Snapshot.Reconciled) return;
            foreach (var plan in LiveTradePlans.Read(account).Where(p => LiveTradePlans.Active(LiveTradePlans.View(account, p, clock.GetUtcNow()))))
            {
                account = await store.ReadAccountAsync(id, token);
                var now = clock.GetUtcNow();
                if (!account.Gate.Armed || account.Gate.Stopped || !account.Snapshot.Reconciled ||
                    plan.MandateVersion != account.Mandate.Version) continue;
                var entry = account.Orders.SingleOrDefault(o => o.Intent.Id == LiveTradePlans.IntentId(plan.Id, OrderSide.Buy));
                if (entry is not null && !LiveTradePlans.Terminal(entry.State))
                {
                    // A confirmed cancel still needs wallet/fill reconciliation and owner reactivation.
                    var currentQuote = account.Snapshot.Quotes.SingleOrDefault(q => q.Symbol == plan.Symbol);
                    var invalidatedPrice = currentQuote is { Bid: > 0 } && currentQuote.ObservedAt <= now &&
                        now - currentQuote.ObservedAt <= TimeSpan.FromSeconds(30) && currentQuote.Bid <= plan.Plan.StopLossPrice;
                    if ((plan.Plan.ExpiresAt <= now || invalidatedPrice) && entry.State is LiveOrderState.Acknowledged or LiveOrderState.Open or LiveOrderState.PartiallyFilled &&
                        entry.CancellationRequestedAt is null)
                        await execution.CancelOwnedAsync(id, entry.Intent.Id, token);
                    continue;
                }
                if (account.Orders.Any(o => !LiveTradePlans.Terminal(o.State))) continue;
                var rules = sizing.SingleOrDefault(s => s.Symbol == plan.Symbol);
                var quote = account.Snapshot.Quotes.SingleOrDefault(q => q.Symbol == plan.Symbol);
                if (rules is null || quote is null || rules.PriceStep <= 0 || rules.BaseStep <= 0 || quote.Bid <= 0 ||
                    quote.Ask < quote.Bid || quote.ObservedAt > now || now - quote.ObservedAt > TimeSpan.FromSeconds(30) ||
                    account.Snapshot.ObservedAt > now || now - account.Snapshot.ObservedAt > TimeSpan.FromSeconds(30)) continue;
                OrderSide side;
                decimal quantity, price;
                if (entry is null)
                {
                    if (LiveTradePlans.View(account, plan, now).Status == "INVALIDATED" || now >= plan.Plan.ExpiresAt) continue;
                    if (quote.Bid <= plan.Plan.StopLossPrice)
                    {
                        await store.WithAccountLockAsync(id, async (tx, ct) =>
                        { tx.InvalidateTradePlan(plan.Id, clock.GetUtcNow()); await tx.CommitAsync(ct); return 0; }, token);
                        continue;
                    }
                    if (quote.Ask > plan.Plan.EntryPrice) continue;
                    side = OrderSide.Buy;
                    price = decimal.Ceiling(quote.Ask / rules.PriceStep) * rules.PriceStep;
                    if (price > plan.Plan.EntryPrice) continue;
                    quantity = plan.Plan.Quantity;
                }
                else
                {
                    if (entry.FilledQuantity <= 0 || account.Orders.Any(o => o.Intent.Id == LiveTradePlans.IntentId(plan.Id, OrderSide.Sell)) ||
                        !(quote.Bid >= plan.Plan.TakeProfitPrice || quote.Bid <= plan.Plan.StopLossPrice || now >= plan.Plan.ExpiresAt)) continue;
                    side = OrderSide.Sell;
                    price = LiveTradePlans.ExitLimitPrice(plan.Plan, quote, rules.PriceStep, account.Mandate.MaximumSlippage);
                    var held = account.Snapshot.Positions.SingleOrDefault(p => p.Symbol == plan.Symbol);
                    // Never sell imported wallet coins or a larger requested buy quantity.
                    quantity = decimal.Floor(entry.FilledQuantity / rules.BaseStep) * rules.BaseStep;
                    if (held is null || held.Quantity - held.ReservedQuantity < quantity || quantity * price < rules.MinimumQuoteSize) continue;
                }
                if (quantity <= 0 || price <= 0) continue;
                var intent = new LiveIntent(id, plan.DecisionId, LiveTradePlans.IntentId(plan.Id, side), TradingMode.Live,
                    plan.Symbol, side, quantity, price, plan.MandateVersion, account.Snapshot.Version, plan.EvidenceHash)
                { TradePlanId = plan.Id, BuyBudget = side == OrderSide.Buy ? quantity * price : null };
                // Durable reservation, current risk checks, lease and exactly-once dispatch all reuse
                // the existing executor. A rejected/uncertain leg requires review, never blind retries.
                await execution.SubmitAsync(intent, token);
                return;
            }
        }
        finally { tick.Release(); }
    }
}
