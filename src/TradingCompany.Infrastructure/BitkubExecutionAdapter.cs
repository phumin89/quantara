using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.Infrastructure;

public enum BitkubBuyHistoryAmount { QuoteBeforeCashFee, QuoteIncludingCashFee, AuthenticatedOrderReceipts }
public enum BitkubBuyBaseRounding { Exact, FloorToBaseStep }
public sealed record BitkubPairExecutionRules(string Symbol, decimal BuyQuoteStep, decimal SellBaseStep,
    decimal PriceStep, decimal MinimumQuoteSize, int RawQuantityScale, decimal RawQuantityStep)
{
    // Opt-in only after broker evidence confirms per-fill received-coin rounding.
    public BitkubBuyBaseRounding BuyBaseRounding { get; init; }
}

// This is server-owned broker-confirmed evidence, never an LLM/browser assertion.
// There is intentionally no default ruleset: unknown sizing/accounting remains blocked.
public sealed record BitkubVerifiedExecutionRules(string ConfirmationReference, string EvidenceHash,
    decimal MaximumCashFeeRate, BitkubBuyHistoryAmount BuyHistoryAmount,
    IReadOnlyList<BitkubPairExecutionRules> Pairs);

public sealed class BitkubExecutionAdapter : ILiveBroker
{
    private readonly BitkubLiveBroker transport;
    private readonly ILiveTradingStore store;
    private readonly LiveAccountId accountId;
    private readonly TimeProvider clock;
    private readonly BitkubVerifiedExecutionRules? rules;
    private readonly string? credentialsPath;
    private readonly Func<bool>? receiptStreamReady;

    public BitkubExecutionAdapter(BitkubLiveBroker transport, ILiveTradingStore store, LiveAccountId accountId,
        TimeProvider clock, BitkubVerifiedExecutionRules? rules, string? credentialsPath = null, Func<bool>? receiptStreamReady = null)
    {
        this.transport = transport;
        this.store = store;
        this.accountId = accountId;
        this.clock = clock;
        this.rules = rules is null ? null : ValidateRules(rules);
        this.credentialsPath = credentialsPath;
        this.receiptStreamReady = receiptStreamReady;
    }

    internal BitkubVerifiedExecutionRules VerifiedRules()
    {
        return ValidateRules(rules ?? throw new InvalidDataException("BITKUB_EXECUTION_RULES_UNVERIFIED"));
    }

    public static BitkubVerifiedExecutionRules ValidateRules(BitkubVerifiedExecutionRules rules)
    {
        if (rules is null || string.IsNullOrWhiteSpace(rules.ConfirmationReference) ||
            rules.EvidenceHash is not { Length: 64 } || rules.EvidenceHash.Any(c => !char.IsAsciiHexDigit(c)) ||
            rules.MaximumCashFeeRate is <= 0 or > .1m || !Enum.IsDefined(rules.BuyHistoryAmount) ||
            rules.Pairs is null || rules.Pairs.Count is 0 or > LiveMarketSymbol.MaximumPayloadMarkets ||
            rules.Pairs.Any(p => p is null || !LiveMarketSymbol.IsValid(p.Symbol) ||
                p.BuyQuoteStep <= 0 || p.SellBaseStep <= 0 || p.PriceStep <= 0 || p.MinimumQuoteSize <= 0 ||
                p.RawQuantityScale is < 0 or > 28 || p.RawQuantityStep <= 0 || !Enum.IsDefined(p.BuyBaseRounding)) ||
            rules.Pairs.Select(p => p.Symbol).Distinct().Count() != rules.Pairs.Count)
            throw new InvalidDataException("BITKUB_EXECUTION_RULES_UNVERIFIED");
        return rules with { Pairs = rules.Pairs.ToArray() };
    }

    internal async Task<LiveAccountState> BoundAccount(CancellationToken token, bool requireExecutionLimits = true)
    {
        var verified = VerifiedRules();
        if (credentialsPath is not null)
        {
            var (key, secret) = BitkubCredentialsFile.Read(credentialsPath);
            var fingerprint = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(key + "\0" + secret)));
            if (fingerprint != transport.CredentialFingerprint) throw new InvalidDataException("BITKUB_ACCOUNT_BINDING_UNVERIFIED");
        }
        var account = await store.ReadAccountAsync(accountId, token);
        if (accountId.Value == Guid.Empty || account.AccountId != accountId || account.Mandate.AccountId != accountId ||
            account.Snapshot.AccountId != accountId || !account.Gate.IsolationVerified ||
            account.Gate.CredentialFingerprint != transport.CredentialFingerprint ||
            account.Mandate.CredentialFingerprint != transport.CredentialFingerprint ||
            (requireExecutionLimits && (account.Mandate.FeeReserveRate < verified.MaximumCashFeeRate ||
                account.Mandate.AllowedSymbols.Any(s => !verified.Pairs.Any(p => p.Symbol == s)))))
            throw new InvalidDataException("BITKUB_ACCOUNT_BINDING_UNVERIFIED");
        return account;
    }

    internal decimal WireAmount(LiveIntent intent)
    {
        var verified = VerifiedRules();
        var pair = verified.Pairs.SingleOrDefault(p => p.Symbol == intent.Symbol)
            ?? throw new InvalidDataException("BITKUB_SYMBOL_UNVERIFIED");
        if (intent.AccountId != accountId || intent.Mode != TradingMode.Live || intent.Quantity <= 0 ||
            intent.LimitPrice <= 0 || intent.LimitPrice % pair.PriceStep != 0 || !Enum.IsDefined(intent.Side))
            throw new InvalidDataException("INVALID_PREPARED_REQUEST");
        if (intent.Side == OrderSide.Buy)
        {
            if (intent.BuyBudget is not { } budget || budget <= 0 || budget != intent.Quantity * intent.LimitPrice)
                throw new InvalidDataException("BITKUB_BUY_BUDGET_REQUIRED");
            var amount = decimal.Floor(budget / (1 + verified.MaximumCashFeeRate) / pair.BuyQuoteStep) * pair.BuyQuoteStep;
            if (amount < pair.MinimumQuoteSize || amount * (1 + verified.MaximumCashFeeRate) > budget)
                throw new InvalidDataException("BITKUB_MINIMUM_QUOTE_REQUIRED");
            return amount;
        }
        if (intent.BuyBudget is not null || intent.Quantity % pair.SellBaseStep != 0 ||
            intent.Quantity * intent.LimitPrice < pair.MinimumQuoteSize)
            throw new InvalidDataException("BITKUB_SELL_SIZING_INVALID");
        return intent.Quantity;
    }

    public async Task<PreparedLiveOrder> PrepareAsync(LiveIntent intent, CancellationToken cancellationToken)
    {
        RequireReceiptStream();
        var amount = WireAmount(intent);
        _ = await BoundAccount(cancellationToken);
        var verified = VerifiedRules();
        var expected = verified.Pairs.Single(p => p.Symbol == intent.Symbol);
        var observed = await transport.GetSymbolRulesAsync(intent.Symbol, cancellationToken);
        var now = clock.GetUtcNow();
        if (observed.ObservedAt > now || now - observed.ObservedAt > TimeSpan.FromSeconds(30) ||
            observed.Status != "active" || observed.Source != "exchange" || observed.MarketSegment != "SPOT" ||
            (intent.Side == OrderSide.Buy ? observed.FreezeBuy : observed.FreezeSell) ||
            observed.PriceStep != expected.PriceStep || observed.MinimumQuoteSize != expected.MinimumQuoteSize ||
            observed.QuantityScale != expected.RawQuantityScale || observed.QuantityStep != expected.RawQuantityStep ||
            (verified.BuyHistoryAmount == BitkubBuyHistoryAmount.AuthenticatedOrderReceipts &&
             (!BitkubProfileVerifier.IsReceiptMarket(observed with { FreezeBuy = false, FreezeSell = false, FreezeCancel = false }, now) ||
              BitkubProfileVerifier.BaseStep(observed) != expected.SellBaseStep)))
            throw new InvalidDataException("BITKUB_MARKET_RULES_CHANGED");
        return intent.Side == OrderSide.Buy ? BitkubLiveBroker.EncodeBudgetLimitRequest(intent, amount)
            : BitkubLiveBroker.EncodeLimitRequest(intent);
    }

    public async Task<BrokerSubmission> PlaceAsync(PreparedLiveOrder request, CancellationToken cancellationToken)
    {
        RequireReceiptStream();
        var amount = WireAmount(request.Intent);
        var account = await BoundAccount(cancellationToken);
        var now = clock.GetUtcNow();
        var owned = account.Orders.SingleOrDefault(o => o.Intent.Id == request.Intent.Id);
        // Executor has already reserved and durably marked dispatch. Recheck binding and freshness.
        if (owned is null || owned.State != LiveOrderState.Dispatching || owned.PreparedRequest != request ||
            owned.Intent != request.Intent || owned.BrokerCredentialFingerprint != transport.CredentialFingerprint ||
            owned.DispatchStartedAt is not { } dispatched || dispatched > now || now - dispatched > TimeSpan.FromSeconds(30) ||
            account.Snapshot.Version != owned.ReservationVersion + 1 ||
            request.Intent.MandateVersion != account.Mandate.Version || account.Gate.ArmedMandateVersion != account.Mandate.Version ||
            account.Mandate.ExpiresAt <= now || !account.Gate.Armed || account.Gate.Stopped || !account.Snapshot.Reconciled ||
            account.Snapshot.ObservedAt > now || now - account.Snapshot.ObservedAt > TimeSpan.FromSeconds(30))
            return new(BrokerSubmissionState.Rejected, null, "BITKUB_DISPATCH_BLOCKED");
        return request.Intent.Side == OrderSide.Buy
            ? await transport.SendBudgetLimitRequestAsync(request, amount, cancellationToken)
            : await transport.SendLimitRequestAsync(request, cancellationToken);
    }

    private void RequireReceiptStream()
    {
        if (VerifiedRules().BuyHistoryAmount == BitkubBuyHistoryAmount.AuthenticatedOrderReceipts && receiptStreamReady?.Invoke() != true)
            throw new InvalidDataException("BITKUB_ORDER_RECEIPT_STREAM_REQUIRED");
    }

    public async Task<BrokerCancellation> CancelAsync(string symbol, string orderId, OrderSide side, CancellationToken cancellationToken)
    {
        // Reduced trading limits must not prevent cancellation of a previously owned order.
        var account = await BoundAccount(cancellationToken, requireExecutionLimits: false);
        // No cancellation of arbitrary/manual orders, even if a caller knows the broker ID.
        if (!account.Orders.Any(o => o.BrokerId == orderId && o.Intent.Symbol == symbol && o.Intent.Side == side &&
            o.BrokerCredentialFingerprint == transport.CredentialFingerprint && o.CancellationRequestedAt is not null &&
            o.CancellationOutcome is null && o.State is LiveOrderState.Acknowledged or LiveOrderState.Open or
                LiveOrderState.PartiallyFilled or LiveOrderState.Unknown))
            throw new InvalidDataException("BITKUB_ORDER_NOT_OWNED");
        return await transport.CancelAsync(symbol, orderId, side, cancellationToken);
    }
}
