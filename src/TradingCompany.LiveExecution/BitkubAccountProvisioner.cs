using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;

namespace TradingCompany.LiveExecution;

// Explicit owner limits plus matching signed wallet observations. Never arms or places.
public sealed class BitkubAccountProvisioner(LiveTradingStore store, BitkubLiveBroker broker,
    BitkubConfiguredAccount configured, ILiveActivationSource activation, TimeProvider clock)
{
    public Task<LiveGateDecision> ProvisionAsync(LiveMandateUpdate request, CancellationToken token)
        => ProvisionCoreAsync(request, token);

    // Owner's explicit full-Spot policy uses actual cash AND owned coins, marked at current bids.
    public Task<LiveGateDecision> ProvisionFullSpotAsync(CancellationToken token)
        => ProvisionCoreAsync(null, token);

    private async Task<LiveGateDecision> ProvisionCoreAsync(LiveMandateUpdate? request, CancellationToken token)
    {
        var id = configured.AccountId;
        var verified = configured.Verification;
        if (request is not null)
        {
            if (request.AccountId != id) return new(false, ["ACCOUNT_MISMATCH"]);
            if (request.ExpectedVersion != 0) return new(false, ["STALE_APPROVAL"]);
            if (!Valid(Mandate(request))) return new(false, ["INVALID_MANDATE"]);
        }
        if (await store.ExistsAsync(id, token)) return new(false, ["ACCOUNT_ALREADY_PROVISIONED"]);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        token = deadline.Token;
        var started = clock.GetUtcNow();
        var wallet = await broker.GetWalletAsync(token);
        var cash = wallet.Single(b => b.Currency == "THB");
        if (wallet.Any(b => b.Reserved != 0)) return new(false, ["INITIAL_ORDERS_NOT_EMPTY"]);
        var symbols = request?.AllowedSymbols ?? verified.Rules.Pairs.Select(p => p.Symbol).ToArray();
        if (wallet.Any(b => b.Currency != "THB" && b.Total != 0 && !symbols.Contains(b.Currency + "-THB", StringComparer.Ordinal)))
            return new(false, ["INITIAL_ASSET_UNSUPPORTED"]);
        var quotes = new List<LiveQuote>();
        if (symbols.Count > 3) quotes.AddRange(await broker.GetQuotesAsync(symbols, token));
        else foreach (var symbol in symbols) quotes.Add(await broker.GetQuoteAsync(symbol, token));
        var now = clock.GetUtcNow();
        if (quotes.Any(q => q.Bid <= 0 || q.Ask < q.Bid || q.ObservedAt > now || now - q.ObservedAt > TimeSpan.FromSeconds(30)) ||
            wallet.Any(b => b.Currency != "THB" && b.Total != 0 && quotes.All(q => q.Symbol != b.Currency + "-THB")))
            return new(false, ["QUOTE_UNAVAILABLE"]);
        var positions = wallet.Where(b => b.Currency != "THB" && b.Total > 0)
            .Select(b => new LivePosition(b.Currency + "-THB", b.Total, 0,
                quotes.Single(q => q.Symbol == b.Currency + "-THB").Bid)).ToArray();
        var equity = cash.Total + positions.Sum(p => p.Quantity * p.MarketPrice);
        if (equity <= 0) return new(false, ["FUNDS_REQUIRED"]);
        request ??= new(id, 0, equity, equity, equity, equity, 1m, 1m, symbols.Count,
            .02m, verified.Rules.MaximumCashFeeRate, symbols, null) { AutomaticDailyRollover = true };
        var mandate = Mandate(request);
        if (!Valid(mandate)) return new(false, ["INVALID_MANDATE"]);
        if (mandate.CapitalAllocation != equity) return new(false, ["CAPITAL_ALLOCATION_MISMATCH"]);
        var opening = new LiveOpeningBalance(started, verified.CredentialFingerprint, verified.BrokerAccountReference,
            cash.Total, positions.Select(p => new LiveBrokerPosition(p.Symbol, p.Quantity, 0)).ToArray());
        var candidate = new LiveAccountState(id, mandate with { AllowedSymbols = mandate.AllowedSymbols.ToArray() },
            new(false, false, verified.CredentialFingerprint, true, 0),
            new(id, 1, started, cash.Total, 0, 0, positions, equity, 0, 0, 0, false, quotes), [], [],
            [new(started, "OwnerAccountProvisioned", null, "Authenticated opening wallet; execution disabled")])
            { BrokerAccountReference = verified.BrokerAccountReference, OpeningBalance = opening };
        var first = await activation.CheckAsync(candidate, token);
        var second = await activation.CheckAsync(candidate, token);
        now = clock.GetUtcNow();
        if (!ValidEvidence(first) || !ValidEvidence(second) || first.ObservedAt > second.ObservedAt ||
            now < started || now - started > TimeSpan.FromSeconds(30)) return new(false, ["PREFLIGHT_UNAVAILABLE"]);
        // Unique DB identity prevents a second onboarding from resetting balances or losses.
        await store.CreateAsync(candidate, token);
        return new(true, []);

        LiveMandate Mandate(LiveMandateUpdate update) => new(id, 1, verified.CredentialFingerprint, update.CapitalAllocation,
            update.MaximumOrderNotional, update.MaximumDailyTurnover, update.MaximumDailyLoss,
            update.MaximumDrawdown, update.MaximumPortfolioExposure, update.MaximumOpenPositions,
            update.MaximumSlippage, update.FeeReserveRate, update.AllowedSymbols, update.ExpiresAt)
            { AutomaticDailyRollover = update.AutomaticDailyRollover };

        bool Valid(LiveMandate value) => LiveRiskPolicy.IsValidMandate(value) && (value.ExpiresAt is null || value.ExpiresAt > clock.GetUtcNow()) &&
            value.FeeReserveRate >= verified.Rules.MaximumCashFeeRate &&
            value.AllowedSymbols.All(s => verified.Rules.Pairs.Any(p => p.Symbol == s));

        bool ValidEvidence(LiveActivationEvidence evidence) => evidence.AccountId == id &&
            evidence.BrokerAccountReference == verified.BrokerAccountReference &&
            evidence.CredentialFingerprint == verified.CredentialFingerprint && evidence.MandateVersion == 1 &&
            evidence.SnapshotVersion == 1 && evidence.CredentialsVerified && evidence.PermissionsVerified &&
            evidence.IsolationVerified && evidence.OpeningBalanceVerified && evidence.ObservedAt >= started &&
            evidence.ObservedAt <= clock.GetUtcNow() && clock.GetUtcNow() - evidence.ObservedAt <= TimeSpan.FromSeconds(30);
    }
}
