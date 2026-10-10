using System.Text.Json;
using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record LiveArmRequest(LiveAccountId AccountId, string BrokerAccountReference,
    long MandateVersion, long SnapshotVersion, bool ConfirmLive, bool ClearStop);
public sealed record LiveMandateUpdate(LiveAccountId AccountId, long ExpectedVersion, decimal CapitalAllocation,
    decimal MaximumOrderNotional, decimal MaximumDailyTurnover, decimal MaximumDailyLoss, decimal MaximumDrawdown,
    decimal MaximumPortfolioExposure, int MaximumOpenPositions, decimal MaximumSlippage, decimal FeeReserveRate,
    IReadOnlyList<string> AllowedSymbols, DateTimeOffset? ExpiresAt)
{
    public bool AutomaticDailyRollover { get; init; }
    public bool UseFullPortfolio { get; init; }
}
public sealed record LivePreflightResult(bool Ready, long MandateVersion, long SnapshotVersion,
    string? BrokerAccountReference, IReadOnlyList<string> Violations);

// Executor-generated, never bound from a browser or model request.
public sealed record LiveActivationEvidence(LiveAccountId AccountId, string BrokerAccountReference,
    string CredentialFingerprint, long MandateVersion, long SnapshotVersion, DateTimeOffset ObservedAt,
    bool CredentialsVerified, bool IsolationVerified, bool PermissionsVerified, bool InitialInventoryEmpty)
{
    public bool OpeningBalanceVerified { get; init; }
}

public interface ILiveActivationSource
{
    Task<LiveActivationEvidence> CheckAsync(LiveAccountState account, CancellationToken cancellationToken);
}

public sealed class LiveOwnerControls(ILiveTradingStore store, ILiveActivationSource preflight, TimeProvider clock, LiveAccountId accountId)
{
    public Task<LiveAccountState> ReadAsync(CancellationToken cancellationToken = default)
        => store.ReadAccountAsync(accountId, cancellationToken);

    public Task<LivePreflightResult> PreflightAsync(CancellationToken cancellationToken = default)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            var evidence = await ReadPreflight(tx, ct);
            if (evidence is null) return new LivePreflightResult(false, tx.Account.Mandate.Version,
                tx.Account.Snapshot.Version, null, ["PREFLIGHT_UNAVAILABLE"]);
            await tx.VerifyLeaseAsync(ct);
            var result = tx.AssessActivation(new(accountId, evidence.BrokerAccountReference,
                tx.Account.Mandate.Version, tx.Account.Snapshot.Version, true, false), evidence, clock.GetUtcNow());
            if (!result.Allowed)
            {
                tx.FailReconciliation("PREFLIGHT_BLOCKED", clock.GetUtcNow());
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await tx.CommitAsync(timeout.Token);
            }
            return new LivePreflightResult(result.Allowed, tx.Account.Mandate.Version,
                tx.Account.Snapshot.Version, evidence.BrokerAccountReference, result.Violations);
        }, cancellationToken);

    public Task<LiveGateDecision> ArmAsync(LiveArmRequest request, CancellationToken cancellationToken = default)
        => ArmCoreAsync(request, cancellationToken);

    // Explicit local owner command only; no HTTP/model route calls this method.
    // Approve the freshly verified snapshot under its lease, while pinning the mandate
    // and binding. This cannot clear Stop or accept changed owner limits.
    public Task<LiveGateDecision> ArmCurrentSnapshotForOwnerCommandAsync(LiveAccountId requestedAccount,
        string brokerAccountReference, long mandateVersion, LiveReconciliation reconciliation,
        CancellationToken cancellationToken = default)
        => ArmCoreAsync(new(requestedAccount, brokerAccountReference, mandateVersion, 0, true, false),
            cancellationToken, reconciliation);

    private Task<LiveGateDecision> ArmCoreAsync(LiveArmRequest request, CancellationToken cancellationToken,
        LiveReconciliation? ownerReconciliation = null)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            if (ownerReconciliation is not null)
            {
                if (request.AccountId != tx.Account.AccountId) return new LiveGateDecision(false, ["ACCOUNT_MISMATCH"]);
                if (request.MandateVersion != tx.Account.Mandate.Version) return new LiveGateDecision(false, ["STALE_APPROVAL"]);
                if (tx.Account.Gate.Stopped) return new LiveGateDecision(false, ["STOPPED"]);
                if (tx.Account.BrokerAccountReference is { } binding && request.BrokerAccountReference != binding)
                    return new LiveGateDecision(false, ["ACCOUNT_BINDING_CHANGED"]);
                var reconciled = await ownerReconciliation.PrepareOwnerActivationUnderLeaseAsync(tx, ct);
                if (!reconciled.Complete)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await tx.CommitAsync(timeout.Token);
                    return new LiveGateDecision(false, reconciled.Violations);
                }
            }
            var evidence = await ReadPreflight(tx, ct);
            if (evidence is null) return new LiveGateDecision(false, ["PREFLIGHT_UNAVAILABLE"]);
            await tx.VerifyLeaseAsync(ct);
            var approved = ownerReconciliation is not null ? request with { SnapshotVersion = tx.Account.Snapshot.Version } : request;
            var result = tx.Arm(approved, evidence, clock.GetUtcNow());
            await tx.CommitAsync(ct);
            return result;
        }, cancellationToken);

    private async Task<LiveActivationEvidence?> ReadPreflight(ILiveAccountTransaction tx, CancellationToken ct)
    {
        try { return await preflight.CheckAsync(tx.Account, ct) ?? throw new InvalidDataException(); }
        catch (Exception)
        {
            tx.FailReconciliation("PREFLIGHT_UNAVAILABLE", clock.GetUtcNow());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await tx.CommitAsync(timeout.Token);
            return null;
        }
    }

    public Task<LiveGateDecision> SaveMandateAsync(LiveMandateUpdate update, CancellationToken cancellationToken = default)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            if (update.AccountId != accountId) return new LiveGateDecision(false, ["ACCOUNT_MISMATCH"]);
            if (update.ExpectedVersion != tx.Account.Mandate.Version || update.ExpectedVersion == long.MaxValue)
                return new LiveGateDecision(false, ["STALE_APPROVAL"]);
            var mandate = new LiveMandate(accountId, update.ExpectedVersion + 1, tx.Account.Mandate.CredentialFingerprint,
                update.CapitalAllocation, update.MaximumOrderNotional, update.MaximumDailyTurnover, update.MaximumDailyLoss,
                update.MaximumDrawdown, update.MaximumPortfolioExposure, update.MaximumOpenPositions,
                update.MaximumSlippage, update.FeeReserveRate, update.AllowedSymbols, update.ExpiresAt)
                { AutomaticDailyRollover = update.AutomaticDailyRollover, UseFullPortfolio = update.UseFullPortfolio };
            var result = tx.SaveMandate(mandate, clock.GetUtcNow());
            if (result.Allowed) await tx.CommitAsync(ct);
            return result;
        }, cancellationToken);

    public Task<LiveGateDecision> SaveMandateAsync(LiveMandate mandate, CancellationToken cancellationToken = default)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            var result = tx.SaveMandate(mandate, clock.GetUtcNow());
            if (result.Allowed) await tx.CommitAsync(ct);
            return result;
        }, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => store.WithAccountLockAsync(accountId, async (tx, ct) =>
        {
            tx.SetStopped(true);
            await tx.CommitAsync(ct);
            return 0;
        }, cancellationToken);
}

public sealed partial class LiveAccountTransaction
{
    public LiveGateDecision SaveMandate(LiveMandate mandate, DateTimeOffset now)
    {
        if (!LiveRiskPolicy.IsValidMandate(mandate) || mandate.ExpiresAt <= now)
            return new(false, ["INVALID_MANDATE"]);
        if (mandate.AccountId != Account.AccountId || mandate.CredentialFingerprint != Account.Gate.CredentialFingerprint ||
            mandate.CredentialFingerprint != Account.Mandate.CredentialFingerprint)
            return new(false, ["ACCOUNT_BINDING_CHANGED"]);
        if (Account.Mandate.Version == long.MaxValue || mandate.Version != Account.Mandate.Version + 1)
            return new(false, ["STALE_APPROVAL"]);
        Account = Account with
        {
            Mandate = mandate with { AllowedSymbols = mandate.AllowedSymbols.ToArray() },
            Gate = Account.Gate with { Armed = false, ArmedMandateVersion = 0 },
            Snapshot = Account.Snapshot with { Reconciled = false, Version = checked(Account.Snapshot.Version + 1) }
        };
        Audit(now, "OwnerMandateSaved", null, JsonSerializer.Serialize(Account.Mandate));
        return new(true, []);
    }

    public LiveGateDecision AssessActivation(LiveArmRequest request, LiveActivationEvidence evidence, DateTimeOffset now)
    {
        var errors = new List<string>();
        var snapshot = Account.Snapshot;
        var mandate = Account.Mandate;
        if (!request.ConfirmLive) errors.Add("OWNER_CONFIRMATION_REQUIRED");
        if (request.AccountId != Account.AccountId || evidence.AccountId != Account.AccountId ||
            snapshot.AccountId != Account.AccountId || mandate.AccountId != Account.AccountId || Account.AccountId.Value == Guid.Empty)
            errors.Add("ACCOUNT_MISMATCH");
        if (string.IsNullOrWhiteSpace(evidence.BrokerAccountReference) ||
            request.BrokerAccountReference != evidence.BrokerAccountReference ||
            (Account.BrokerAccountReference is not null && Account.BrokerAccountReference != evidence.BrokerAccountReference) ||
            string.IsNullOrWhiteSpace(evidence.CredentialFingerprint) ||
            evidence.CredentialFingerprint != Account.Gate.CredentialFingerprint ||
            evidence.CredentialFingerprint != mandate.CredentialFingerprint)
            errors.Add("ACCOUNT_BINDING_CHANGED");
        if (request.MandateVersion != mandate.Version || evidence.MandateVersion != mandate.Version ||
            request.SnapshotVersion != snapshot.Version || evidence.SnapshotVersion != snapshot.Version)
            errors.Add("STALE_APPROVAL");
        if (!LiveRiskPolicy.IsValidMandate(mandate)) errors.Add("INVALID_MANDATE");
        if (mandate.ExpiresAt <= now) errors.Add("MANDATE_EXPIRED");
        if (!evidence.CredentialsVerified) errors.Add("CREDENTIALS_REQUIRED");
        if (!evidence.IsolationVerified || !Account.Gate.IsolationVerified) errors.Add("ISOLATION_REQUIRED");
        if (!evidence.PermissionsVerified) errors.Add("PERMISSION_VERIFICATION_REQUIRED");
        if (evidence.ObservedAt > now || now - evidence.ObservedAt > TimeSpan.FromSeconds(30)) errors.Add("STALE_PREFLIGHT");
        if (snapshot.ObservedAt > now || now - snapshot.ObservedAt > TimeSpan.FromSeconds(30)) errors.Add("STALE_SNAPSHOT");
        if (!snapshot.Reconciled) errors.Add("RECONCILIATION_REQUIRED");
        if (Account.PerformanceBaseline is not { } baseline || baseline.DayStartEquity <= 0 ||
            baseline.HighWaterEquity < baseline.DayStartEquity || baseline.BangkokDate != DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime))
            errors.Add("DAILY_BASELINE_REQUIRED");
        if (Account.Gate.Stopped && !request.ClearStop) errors.Add("STOPPED");
        if (Account.Orders.Any(o => o.State is LiveOrderState.Reserved or LiveOrderState.Dispatching or LiveOrderState.Unknown ||
            (o.CancellationRequestedAt is not null && o.State is not (LiveOrderState.Filled or LiveOrderState.Cancelled or LiveOrderState.Rejected) &&
             o.CancellationOutcome != BrokerSubmissionState.Rejected))) errors.Add("UNRESOLVED_ORDER");
        if (Account.FirstActivatedAt is null &&
            (!(Account.OpeningBalance is null && snapshot.Positions.Count == 0 && evidence.InitialInventoryEmpty ||
                evidence.OpeningBalanceVerified && MatchesOpeningBalance(now)) ||
            snapshot.CashReserved != 0 || snapshot.PendingBuyNotional != 0 || snapshot.PendingSellNotional != 0 ||
            Account.Fills.Count != 0 || Account.Orders.Any(o => o.State != LiveOrderState.Rejected)))
            errors.Add("INITIAL_INVENTORY_NOT_EMPTY");
        try
        {
            if (snapshot.CashBalance < 0 || snapshot.CashReserved < 0 || snapshot.CashReserved > snapshot.CashBalance ||
                snapshot.Version <= 0 || snapshot.Equity <= 0 ||
                snapshot.Positions.Any(p => p.Quantity <= 0 || p.ReservedQuantity < 0 || p.ReservedQuantity > p.Quantity || p.MarketPrice <= 0) ||
                snapshot.Equity != snapshot.CashBalance + snapshot.Positions.Sum(p => p.Quantity * p.MarketPrice))
                errors.Add("INVALID_SNAPSHOT");
        }
        catch (OverflowException) { errors.Add("INVALID_SNAPSHOT"); }
        return new(errors.Count == 0, errors.Distinct().ToArray());
    }

    private bool MatchesOpeningBalance(DateTimeOffset now)
    {
        var opening = Account.OpeningBalance;
        var snapshot = Account.Snapshot;
        if (opening is null || opening.ObservedAt > snapshot.ObservedAt || opening.ObservedAt > now ||
            opening.CredentialFingerprint != Account.Gate.CredentialFingerprint ||
            opening.CredentialFingerprint != Account.Mandate.CredentialFingerprint ||
            string.IsNullOrWhiteSpace(opening.BrokerAccountReference) || opening.BrokerAccountReference != Account.BrokerAccountReference ||
            opening.CashBalance < 0 || opening.CashBalance != snapshot.CashBalance || opening.Positions is null ||
            opening.Positions.Count != snapshot.Positions.Count ||
            opening.Positions.Any(p => p is null) ||
            opening.Positions.Select(p => p.Symbol).Distinct(StringComparer.Ordinal).Count() != opening.Positions.Count)
            return false;
        return opening.Positions.All(p => p.Quantity > 0 && p.ReservedQuantity == 0 &&
            Account.Mandate.AllowedSymbols.Contains(p.Symbol, StringComparer.Ordinal) &&
            snapshot.Positions.Any(s => s.Symbol == p.Symbol && s.Quantity == p.Quantity && s.ReservedQuantity == 0));
    }

    public LiveGateDecision Arm(LiveArmRequest request, LiveActivationEvidence evidence, DateTimeOffset now)
    {
        var decision = AssessActivation(request, evidence, now);
        if (!decision.Allowed)
        {
            Account = Account with { Gate = Account.Gate with { Armed = false, ArmedMandateVersion = 0 } };
            Audit(now, "OwnerArmBlocked", null, string.Join(",", decision.Violations));
            return decision;
        }
        if (Account.Gate.Stopped) Audit(now, "OwnerClearedStop", null, "Explicit activation confirmation");
        Account = Account with
        {
            Gate = Account.Gate with { Armed = true, Stopped = false, ArmedMandateVersion = Account.Mandate.Version },
            FirstActivatedAt = Account.FirstActivatedAt ?? now,
            BrokerAccountReference = evidence.BrokerAccountReference
        };
        Audit(now, "OwnerArmed", null, JsonSerializer.Serialize(new { request, evidence }));
        return new(true, []);
    }
}
