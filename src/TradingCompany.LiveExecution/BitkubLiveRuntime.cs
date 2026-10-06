using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;

namespace TradingCompany.LiveExecution;

public sealed record BitkubLiveVerification(string BrokerAccountReference, string CredentialFingerprint,
    string ConfirmationReference, string EvidenceHash, bool AccountBindingVerified, bool TradePermissionVerified,
    BitkubVerifiedExecutionRules Rules);
public sealed record BitkubConfiguredAccount(LiveAccountId AccountId, BitkubLiveVerification Verification);

public static class BitkubLiveRuntime
{
    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("LiveExecution:Enabled")) return;
        var account = new LiveAccountId(RequireGuid(configuration["LiveExecution:AccountId"]));
        var connection = Required(configuration["ConnectionStrings:LiveTrading"]);
        var verificationPath = Absolute(configuration["LiveExecution:VerificationFile"]);
        var credentialsPath = Absolute(configuration["Bitkub:CredentialsFile"]);
        var verification = ReadVerification(verificationPath);
        ValidateVerification(verification);
        verification = verification with { Rules = BitkubExecutionAdapter.ValidateRules(verification.Rules) };
        services.AddSingleton(new BitkubConfiguredAccount(account, verification));
        var coordinatorToken = configuration["LiveExecution:CoordinatorToken"] ?? "";
        services.AddOptions<CoordinatorAccessOptions>(CoordinatorAccess.SchemeName).Configure(options =>
        {
            options.AccountId = account;
            options.Token = coordinatorToken;
        });
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<BitkubLiveBroker>(sp =>
        {
            var (key, secret) = BitkubCredentialsFile.Read(credentialsPath);
            var fingerprint = Fingerprint(key, secret);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(fingerprint),
                Encoding.ASCII.GetBytes(verification.CredentialFingerprint))) throw Invalid();
            return new BitkubLiveBroker(key, secret);
        });
        services.AddSingleton(_ => new LiveTradingStore(connection));
        services.AddSingleton<ILiveTradingStore>(sp => sp.GetRequiredService<LiveTradingStore>());
        var usesReceipts = verification.Rules.BuyHistoryAmount == BitkubBuyHistoryAmount.AuthenticatedOrderReceipts;
        if (usesReceipts)
        {
            services.AddSingleton(_ => new BitkubOrderReceiptJournal(connection, account, verification.CredentialFingerprint));
            services.AddSingleton<BitkubReceiptObserver>();
        }
        services.AddSingleton<BitkubExecutionAdapter>(sp => new BitkubExecutionAdapter(
            sp.GetRequiredService<BitkubLiveBroker>(), sp.GetRequiredService<ILiveTradingStore>(), account,
            sp.GetRequiredService<TimeProvider>(), verification.Rules, credentialsPath,
            usesReceipts ? () => sp.GetRequiredService<BitkubReceiptObserver>().Ready : null));
        services.AddSingleton<ILiveBroker>(sp => sp.GetRequiredService<BitkubExecutionAdapter>());
        services.AddSingleton<BitkubReconciliationSource>(sp => new BitkubReconciliationSource(
            sp.GetRequiredService<BitkubLiveBroker>(), sp.GetRequiredService<BitkubExecutionAdapter>(), account,
            sp.GetRequiredService<TimeProvider>(), sp.GetService<BitkubOrderReceiptJournal>()));
        services.AddSingleton<ILiveReconciliationSource>(sp => sp.GetRequiredService<BitkubReconciliationSource>());
        services.AddSingleton<ILiveActivationSource>(sp => new BitkubActivationSource(
            sp.GetRequiredService<BitkubLiveBroker>(), account,
            verification, credentialsPath, sp.GetRequiredService<TimeProvider>(),
            usesReceipts ? () => sp.GetRequiredService<BitkubReceiptObserver>().Ready : null));
        services.AddSingleton(sp => new LiveOwnerControls(sp.GetRequiredService<ILiveTradingStore>(),
            sp.GetRequiredService<ILiveActivationSource>(), sp.GetRequiredService<TimeProvider>(), account));
        services.AddSingleton<LiveExecutionService>();
        services.AddSingleton<LiveReconciliation>();
        services.AddSingleton<BitkubAccountProvisioner>();
        services.AddHostedService<LiveSchemaService>();
        if (usesReceipts) services.AddHostedService(sp => sp.GetRequiredService<BitkubReceiptObserver>());
    }

    private static BitkubLiveVerification ReadVerification(string path)
    {
        try
        {
            var buffer = new byte[64 * 1024 + 1];
            using var stream = File.OpenRead(path);
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (length > 64 * 1024) throw Invalid();
            var bytes = buffer.AsMemory(0, length);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            RejectDuplicates(document.RootElement);
            return JsonSerializer.Deserialize<BitkubLiveVerification>(bytes.Span, new JsonSerializerOptions
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false }) ?? throw Invalid();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or OverflowException)
        { throw Invalid(); }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw Invalid(); RejectDuplicates(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var x in element.EnumerateArray()) RejectDuplicates(x);
    }

    private static void ValidateVerification(BitkubLiveVerification v)
    {
        if (!ValidReference(v.BrokerAccountReference) || !ValidReference(v.ConfirmationReference) ||
            v.EvidenceHash is not { Length: 64 } || v.EvidenceHash.Any(c => !char.IsAsciiHexDigit(c)) ||
            !v.AccountBindingVerified || !v.TradePermissionVerified ||
            v.CredentialFingerprint is not { Length: 71 } || !v.CredentialFingerprint.StartsWith("sha256:", StringComparison.Ordinal) ||
            v.CredentialFingerprint[7..].Any(c => !char.IsAsciiHexDigit(c)) || v.Rules is null) throw Invalid();
        try { _ = BitkubExecutionAdapter.ValidateRules(v.Rules); }
        catch (InvalidDataException) { throw Invalid(); }
    }

    private static bool ValidReference(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);

    private static Guid RequireGuid(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : throw Invalid();
    private static string Required(string? value) => !string.IsNullOrWhiteSpace(value) ? value : throw Invalid();
    private static string Absolute(string? value) => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) ? value : throw Invalid();
    private static string Fingerprint(string key, string secret) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "\0" + secret)));
    private static InvalidOperationException Invalid() => new("Live execution configuration is invalid.");
}

internal sealed class BitkubActivationSource(BitkubLiveBroker broker, LiveAccountId accountId,
    BitkubLiveVerification verification, string credentialsPath, TimeProvider clock,
    Func<bool>? receiptStreamReady = null) : ILiveActivationSource
{
    public async Task<LiveActivationEvidence> CheckAsync(LiveAccountState account, CancellationToken cancellationToken)
    {
        var started = clock.GetUtcNow();
        if (verification.Rules.BuyHistoryAmount == BitkubBuyHistoryAmount.AuthenticatedOrderReceipts && receiptStreamReady?.Invoke() != true)
            throw new InvalidDataException("BITKUB_ORDER_RECEIPT_STREAM_REQUIRED");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        cancellationToken = deadline.Token;
        if (account.AccountId != accountId || account.Gate.CredentialFingerprint != verification.CredentialFingerprint ||
            account.Mandate.CredentialFingerprint != verification.CredentialFingerprint) throw new InvalidDataException("Activation unavailable.");
        var (key, secret) = BitkubCredentialsFile.Read(credentialsPath);
        var fingerprint = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "\0" + secret)));
        if (fingerprint != verification.CredentialFingerprint) throw new InvalidDataException("Activation unavailable.");
        // The saved profile binds the account, but it cannot prove permission that
        // may have been revoked since startup. All owner activation paths use this probe.
        var tradePermission = await broker.CheckTradePermissionAsync(cancellationToken);
        var wallet = await broker.GetWalletAsync(cancellationToken);
        var opens = new List<BrokerOpenOrderEvidence>();
        foreach (var pair in verification.Rules.Pairs) opens.AddRange(await broker.GetOpenOrdersAsync(pair.Symbol, cancellationToken));
        var cash = wallet.Single(b => b.Currency == "THB");
        // Local risk reservations retain fee headroom until terminal reconciliation.
        // Broker reservations must match its open orders and fit inside that headroom.
        if (cash.Total != account.Snapshot.CashBalance || cash.Reserved > account.Snapshot.CashReserved ||
            cash.Reserved != opens.Where(o => o.Side == OrderSide.Buy).Sum(o => o.RawAmount) ||
            opens.Select(o => o.OrderId).Distinct(StringComparer.Ordinal).Count() != opens.Count ||
            wallet.Where(b => b.Currency != "THB" && b.Total != 0).Any(b =>
                !account.Snapshot.Positions.Any(p => p.Symbol == b.Currency + "-THB" && p.Quantity == b.Total && p.ReservedQuantity == b.Reserved)) ||
            account.Snapshot.Positions.Any(p => !wallet.Any(b => b.Currency + "-THB" == p.Symbol && b.Total == p.Quantity && b.Reserved == p.ReservedQuantity)) ||
            opens.Any(o => o.Type != "limit" || o.RawAmount <= 0 || !account.Orders.Any(l =>
                l.State is LiveOrderState.Acknowledged or LiveOrderState.Open or LiveOrderState.PartiallyFilled &&
                l.BrokerId == o.OrderId && l.Intent.Id.ToString("N") == o.ClientId &&
                l.Intent.Symbol == o.Symbol && l.Intent.Side == o.Side && l.Intent.LimitPrice == o.Rate &&
                (o.Side == OrderSide.Buy ? o.RawAmount <= l.ReservedCash : o.RawAmount == l.ReservedQuantity))) ||
            account.Orders.Any(l => l.State is LiveOrderState.Acknowledged or LiveOrderState.Open or LiveOrderState.PartiallyFilled &&
                !opens.Any(o => o.OrderId == l.BrokerId && o.ClientId == l.Intent.Id.ToString("N"))))
            throw new InvalidDataException("Activation unavailable.");
        var empty = wallet.All(b => b.Currency == "THB" || b.Total == 0) && cash.Reserved == 0 && opens.Count == 0;
        var openingVerified = account.OpeningBalance is { } opening &&
            opening.ObservedAt <= started && opening.CredentialFingerprint == fingerprint &&
            opening.BrokerAccountReference == verification.BrokerAccountReference && opening.CashBalance == cash.Total &&
            cash.Reserved == 0 && opens.Count == 0 && opening.Positions is not null &&
            opening.Positions.Select(p => p.Symbol).Distinct(StringComparer.Ordinal).Count() == opening.Positions.Count &&
            opening.Positions.Count == wallet.Count(b => b.Currency != "THB" && b.Total != 0) &&
            opening.Positions.All(p => p.Quantity > 0 && p.ReservedQuantity == 0 &&
                verification.Rules.Pairs.Any(r => r.Symbol == p.Symbol) &&
                wallet.Any(b => b.Currency + "-THB" == p.Symbol && b.Total == p.Quantity && b.Reserved == 0));
        return new(accountId, verification.BrokerAccountReference, fingerprint, account.Mandate.Version, account.Snapshot.Version,
            started, true, account.Gate.IsolationVerified, tradePermission, empty) { OpeningBalanceVerified = openingVerified };
    }
}

internal sealed class LiveSchemaService(LiveTradingStore store) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    { try { await store.EnsureSchemaAsync(cancellationToken); } catch { throw new InvalidOperationException("Live storage initialization failed."); } }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
