using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;

namespace TradingCompany.LiveExecution;

// Explicit owner limits plus two matching signed wallet observations. Never arms or places.
public sealed class BitkubAccountProvisioner(LiveTradingStore store, BitkubLiveBroker broker,
    BitkubConfiguredAccount configured, ILiveActivationSource activation, TimeProvider clock)
{
    public async Task<LiveGateDecision> ProvisionAsync(LiveMandateUpdate request, CancellationToken token)
    {
        var id = configured.AccountId;
        var verified = configured.Verification;
        if (request.AccountId != id) return new(false, ["ACCOUNT_MISMATCH"]);
        if (request.ExpectedVersion != 0) return new(false, ["STALE_APPROVAL"]);
        var mandate = new LiveMandate(id, 1, verified.CredentialFingerprint, request.CapitalAllocation,
            request.MaximumOrderNotional, request.MaximumDailyTurnover, request.MaximumDailyLoss,
            request.MaximumDrawdown, request.MaximumPortfolioExposure, request.MaximumOpenPositions,
            request.MaximumSlippage, request.FeeReserveRate, request.AllowedSymbols, request.ExpiresAt);
        if (!LiveRiskPolicy.IsValidMandate(mandate) || mandate.ExpiresAt <= clock.GetUtcNow() ||
            mandate.FeeReserveRate < verified.Rules.MaximumCashFeeRate ||
            mandate.AllowedSymbols.Any(s => !verified.Rules.Pairs.Any(p => p.Symbol == s)))
            return new(false, ["INVALID_MANDATE"]);
        if (await store.ExistsAsync(id, token)) return new(false, ["ACCOUNT_ALREADY_PROVISIONED"]);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        token = deadline.Token;
        var started = clock.GetUtcNow();
        var wallet = await broker.GetWalletAsync(token);
        var cash = wallet.Single(b => b.Currency == "THB");
        if (cash.Total <= 0 || cash.Reserved != 0 || wallet.Any(b => b.Currency != "THB" && b.Total != 0))
            return new(false, ["INITIAL_INVENTORY_NOT_EMPTY"]);
        // This first version accounts for the entire isolated THB wallet. Extra deposits require reconciliation.
        if (mandate.CapitalAllocation != cash.Total) return new(false, ["CAPITAL_ALLOCATION_MISMATCH"]);
        var quotes = new List<LiveQuote>();
        foreach (var symbol in mandate.AllowedSymbols) quotes.Add(await broker.GetQuoteAsync(symbol, token));
        var candidate = new LiveAccountState(id, mandate with { AllowedSymbols = mandate.AllowedSymbols.ToArray() },
            new(false, false, verified.CredentialFingerprint, true, 0),
            new(id, 1, started, cash.Total, 0, 0, [], cash.Total, 0, 0, 0, false, quotes), [], [],
            [new(started, "OwnerAccountProvisioned", null, "Verified isolated wallet; execution disabled")])
            { BrokerAccountReference = verified.BrokerAccountReference };
        var first = await activation.CheckAsync(candidate, token);
        var second = await activation.CheckAsync(candidate, token);
        var now = clock.GetUtcNow();
        if (!Valid(first) || !Valid(second) || first.ObservedAt > second.ObservedAt ||
            now < started || now - started > TimeSpan.FromSeconds(30)) return new(false, ["PREFLIGHT_UNAVAILABLE"]);
        // Unique DB identity prevents a second onboarding from resetting balances or losses.
        await store.CreateAsync(candidate, token);
        return new(true, []);

        bool Valid(LiveActivationEvidence evidence) => evidence.AccountId == id &&
            evidence.BrokerAccountReference == verified.BrokerAccountReference &&
            evidence.CredentialFingerprint == verified.CredentialFingerprint && evidence.MandateVersion == 1 &&
            evidence.SnapshotVersion == 1 && evidence.CredentialsVerified && evidence.PermissionsVerified &&
            evidence.IsolationVerified && evidence.InitialInventoryEmpty && evidence.ObservedAt >= started &&
            evidence.ObservedAt <= clock.GetUtcNow() && clock.GetUtcNow() - evidence.ObservedAt <= TimeSpan.FromSeconds(30);
    }
}
