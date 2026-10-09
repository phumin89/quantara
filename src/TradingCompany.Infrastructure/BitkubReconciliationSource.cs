using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.Infrastructure;

// Produces account-scoped normalized evidence. It never places, cancels or arms.
public sealed class BitkubReconciliationSource(BitkubLiveBroker transport, BitkubExecutionAdapter execution,
    LiveAccountId accountId, TimeProvider clock, BitkubOrderReceiptJournal? receipts = null) : ILiveReconciliationSource
{
    public async Task<LiveBrokerEvidence> ReadAsync(LiveAccountId requestedAccount, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        cancellationToken = deadline.Token;
        if (requestedAccount != accountId) throw Unavailable();
        var rules = execution.VerifiedRules();
        if (rules.BuyHistoryAmount == BitkubBuyHistoryAmount.AuthenticatedOrderReceipts)
        {
            if (receipts is null) throw Unavailable();
            await receipts.CheckHealthyAsync(cancellationToken);
        }
        var account = await execution.BoundAccount(cancellationToken, requireExecutionLimits: false);
        var started = clock.GetUtcNow();
        var epoch = account.OpeningBalance?.ObservedAt ?? account.FirstActivatedAt ?? account.Snapshot.ObservedAt;
        // The published REST archive boundary is 90 days. Never call a truncated history complete.
        if (epoch > started || started - epoch >= TimeSpan.FromDays(90)) throw Unavailable();
        var expected = account.Orders.Where(o => o.State != LiveOrderState.Rejected).ToArray();
        var orders = new List<LiveBrokerOrder>();
        var fills = new List<BrokerFill>();
        var quotes = new List<LiveQuote>();

        var symbols = rules.Pairs.Select(p => p.Symbol).ToArray();
        var observed = symbols.Length > 3 ? (await BitkubLiveBroker.ReadMarketBatchesAsync(symbols, async (symbol, token) =>
            (Symbol: symbol, Open: await transport.GetOpenOrdersAsync(symbol, token),
                History: await transport.GetHistoryAsync(symbol, token)), cancellationToken)).ToDictionary(x => x.Symbol, StringComparer.Ordinal) : null;
        if (symbols.Length > 3) quotes.AddRange(await transport.GetQuotesAsync(symbols, cancellationToken));

        foreach (var pair in rules.Pairs)
        {
            var open = observed is null ? await transport.GetOpenOrdersAsync(pair.Symbol, cancellationToken) : observed[pair.Symbol].Open;
            var history = (observed is null ? await transport.GetHistoryAsync(pair.Symbol, cancellationToken) : observed[pair.Symbol].History)
                .Where(h => h.Timestamp >= epoch.ToUnixTimeMilliseconds()).ToArray();
            if (history.Any(h => h.Timestamp > clock.GetUtcNow().ToUnixTimeMilliseconds())) throw Unavailable();
            var local = expected.Where(o => o.Intent.Symbol == pair.Symbol).ToArray();
            if (open.Any(o => !local.Any(l => l.Intent.Id.ToString("N") == o.ClientId && l.Intent.Side == o.Side)) ||
                history.Any(h => !local.Any(l => l.Intent.Id.ToString("N") == h.ClientId &&
                    (l.Intent.Side == OrderSide.Buy ? "buy" : "sell") == h.Side))) throw Unavailable();
            if (observed is null) quotes.Add(await transport.GetQuoteAsync(pair.Symbol, cancellationToken));

            foreach (var owned in local)
            {
                var clientId = owned.Intent.Id.ToString("N");
                var matchingOpen = open.Where(o => o.ClientId == clientId && o.Side == owned.Intent.Side).ToArray();
                var matchingHistory = history.Where(h => h.ClientId == clientId).ToArray();
                if (matchingOpen.Length > 1) throw Unavailable();
                var identities = matchingOpen.Select(o => o.OrderId)
                    .Concat(matchingHistory.Select(h => h.OrderId)).Distinct().ToArray();
                // A missing order is never a rejection, and ambiguous lineage is never guessed.
                var brokerId = owned.BrokerId ?? (identities.Length == 1 ? identities[0] : throw Unavailable());
                var info = await transport.GetOrderAsync(pair.Symbol, brokerId, owned.Intent.Side, cancellationToken);
                if (info.ClientId != clientId || info.Rate != owned.Intent.LimitPrice || info.RawAmount != execution.WireAmount(owned.Intent) ||
                    matchingOpen.Any(o => o.OrderId != brokerId || o.Rate != info.Rate || o.Type != "limit") ||
                    info.History.Count != matchingHistory.Length) throw Unavailable();

                var orderFills = new List<BrokerFill>();
                IReadOnlyList<BrokerFill>? receivedFills = null;
                decimal? settledSellQuote = null;
                if (rules.BuyHistoryAmount == BitkubBuyHistoryAmount.AuthenticatedOrderReceipts && info.History.Count > 0)
                {
                    var receipt = receipts is null ? null : await receipts.ReadLatestAsync(brokerId, clientId, cancellationToken);
                    receivedFills = BitkubOrderReceipts.Normalize(accountId, owned.Intent, info, rules, receipt ?? throw Unavailable());
                    if (owned.Intent.Side == OrderSide.Sell)
                        settledSellQuote = receipt!.ReceivedAmount + receipt.CashFee;
                }
                foreach (var raw in info.History)
                {
                    var corroborated = matchingHistory.SingleOrDefault(h => h.TransactionId == raw.TransactionId);
                    if (corroborated is null || corroborated.OrderId != raw.OrderId || corroborated.Type != "limit" ||
                        corroborated.Rate != raw.Rate || corroborated.RawAmount != raw.RawAmount ||
                        corroborated.ReportedFee != raw.ReportedFee || corroborated.FeeCredit != raw.FeeCredit ||
                        corroborated.Timestamp != raw.Timestamp) throw Unavailable();
                    var fill = receivedFills is null ? Normalize(accountId, owned.Intent, brokerId, raw, rules)
                        : receivedFills.Single(f => f.TransactionId == raw.TransactionId);
                    var previous = account.Fills.SingleOrDefault(f => f.TransactionId == fill.TransactionId);
                    // REST has no correction revision; conflicting history requires investigation.
                    if (previous is not null && previous != fill) throw Unavailable();
                    orderFills.Add(fill);
                }
                var quantity = orderFills.Sum(f => f.Quantity);
                var terminal = info.Status is "filled" or "cancelled";
                if (terminal && (matchingOpen.Length != 0 || (info.Status == "filled" && info.RawRemaining != 0))) throw Unavailable();
                if (!terminal && matchingOpen.Length != 1) throw Unavailable();
                var state = info.Status == "filled" ? LiveOrderState.Filled : info.Status == "cancelled" ? LiveOrderState.Cancelled :
                    quantity > 0 ? LiveOrderState.PartiallyFilled : LiveOrderState.Open;
                var reservedCash = !terminal && owned.Intent.Side == OrderSide.Buy ? info.RawRemaining : 0;
                var reservedCoins = !terminal && owned.Intent.Side == OrderSide.Sell ? info.RawRemaining : 0;
                if (!terminal && matchingOpen[0].RawAmount != info.RawRemaining) throw Unavailable();
                orders.Add(new(clientId, brokerId, pair.Symbol, owned.Intent.Side, owned.Intent.Quantity,
                    owned.Intent.LimitPrice, state, quantity, reservedCash, reservedCoins)
                    { BuyBudget = owned.Intent.BuyBudget, SettledSellQuoteAmount = settledSellQuote });
                fills.AddRange(orderFills);
            }
        }

        // A large universe takes time to scan. Timestamp the actual final wallet request,
        // not the first market request; quotes keep their own timestamps and the whole
        // collection remains bounded by the 25-second deadline and retained start time.
        var walletObservedAt = clock.GetUtcNow();
        var wallet = await transport.GetWalletAsync(cancellationToken);
        var cash = wallet.Single(b => b.Currency == "THB");
        var positions = new List<LiveBrokerPosition>();
        foreach (var balance in wallet.Where(b => b.Currency != "THB" && b.Total != 0))
        {
            var symbol = balance.Currency + "-THB";
            if (!rules.Pairs.Any(p => p.Symbol == symbol)) throw Unavailable();
            positions.Add(new(symbol, balance.Total, balance.Reserved));
        }
        if (expected.Length != orders.Count || fills.Select(f => f.TransactionId).Distinct().Count() != fills.Count ||
            cash.Reserved != orders.Sum(o => o.ReservedCash) ||
            positions.Any(p => p.ReservedQuantity != orders.Where(o => o.Symbol == p.Symbol).Sum(o => o.ReservedQuantity)) ||
            started > clock.GetUtcNow() || clock.GetUtcNow() - started > TimeSpan.FromSeconds(30)) throw Unavailable();
        return new(accountId, transport.CredentialFingerprint, walletObservedAt, true, cash.Total, cash.Reserved, positions, orders, fills, quotes)
            { CollectionStartedAt = started };
    }

    internal static BrokerFill Normalize(LiveAccountId account, LiveIntent intent, string brokerId,
        BrokerOrderFillEvidence raw, BitkubVerifiedExecutionRules rules)
    {
        if (!Enum.IsDefined(rules.BuyHistoryAmount) || rules.BuyHistoryAmount == BitkubBuyHistoryAmount.AuthenticatedOrderReceipts) throw Unavailable();
        if (raw.RawAmount <= 0 || raw.Rate <= 0 || raw.ReportedFee < 0 || raw.FeeCredit < 0 ||
            raw.FeeCredit > raw.ReportedFee || raw.Timestamp <= 0 || string.IsNullOrWhiteSpace(raw.TransactionId)) throw Unavailable();
        var fee = raw.ReportedFee - raw.FeeCredit;
        var gross = intent.Side == OrderSide.Buy
            ? raw.RawAmount - (rules.BuyHistoryAmount == BitkubBuyHistoryAmount.QuoteIncludingCashFee ? fee : 0)
            : raw.RawAmount * raw.Rate;
        var feeBasis = intent.Side == OrderSide.Buy ? raw.RawAmount : gross;
        if (gross <= 0 || fee > feeBasis * rules.MaximumCashFeeRate) throw Unavailable();
        var pair = rules.Pairs.SingleOrDefault(p => p.Symbol == intent.Symbol) ?? throw Unavailable();
        if (pair.SellBaseStep <= 0 || !Enum.IsDefined(pair.BuyBaseRounding)) throw Unavailable();
        var quantity = intent.Side == OrderSide.Buy ? gross / raw.Rate : raw.RawAmount;
        if (intent.Side == OrderSide.Buy && pair.BuyBaseRounding == BitkubBuyBaseRounding.FloorToBaseStep)
        {
            if (intent.BuyBudget is null) throw Unavailable();
            quantity = decimal.Floor(quantity / pair.SellBaseStep) * pair.SellBaseStep;
            // Check the exact remainder, including division at decimal's precision boundary.
            var remainder = gross - quantity * raw.Rate;
            if (remainder < 0 || remainder >= pair.SellBaseStep * raw.Rate) throw Unavailable();
        }
        else if (quantity * raw.Rate != gross) throw Unavailable();
        if (quantity <= 0 || quantity % pair.SellBaseStep != 0) throw Unavailable();
        return new(account, raw.TransactionId, brokerId, intent.Side, intent.Symbol, quantity, raw.Rate, fee, 1)
        { QuoteAmount = quantity * raw.Rate == gross ? null : gross };
    }

    private static InvalidDataException Unavailable() => new("BITKUB_RECONCILIATION_EVIDENCE_UNVERIFIED");
}
