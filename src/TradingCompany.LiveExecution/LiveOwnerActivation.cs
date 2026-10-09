using TradingCompany.Application;
using TradingCompany.Infrastructure;

namespace TradingCompany.LiveExecution;

public sealed record LiveOwnerActivationResult(bool Activated, IReadOnlyList<string> Reasons);

// Explicit local owner command only. No API/model caller receives this capability.
// Uses the existing owner controls and account lock; never places or cancels an order.
public sealed class LiveOwnerActivation(BitkubConfiguredAccount configured, LiveTradingStore store,
    BitkubLiveBroker broker, BitkubAccountProvisioner provisioner, LiveReconciliation reconciliation,
    LiveOwnerControls controls, TimeProvider clock)
{
    public async Task<LiveOwnerActivationResult> ActivateFullSpotAsync(CancellationToken token, decimal? maximumOrderNotional = null,
        bool useFundedOrderCeiling = false)
    {
        if (maximumOrderNotional is <= 0) return new(false, ["MAXIMUM_ORDER_NOTIONAL_REQUIRED"]);
        if (useFundedOrderCeiling && maximumOrderNotional is not null) return new(false, ["CONFLICTING_ORDER_CEILINGS"]);
        if (!await broker.CheckTradePermissionAsync(token)) return new(false, ["TRADE_PERMISSION_REQUIRED"]);
        if (!await store.ExistsAsync(configured.AccountId, token))
        {
            var provisioned = await provisioner.ProvisionFullSpotAsync(token);
            if (!provisioned.Allowed) return new(false, provisioned.Violations);
        }
        var account = await controls.ReadAsync(token);
        if (account.Gate.Stopped) return new(false, ["OWNER_STOP_ACTIVE"]);
        var symbols = configured.Verification.Rules.Pairs.Select(p => p.Symbol).Order(StringComparer.Ordinal).ToArray();
        if (account.Mandate.AllowedSymbols.Any(s => !symbols.Contains(s)))
            return new(false, ["MARKET_REMOVAL_REQUIRES_MANDATE_UPDATE"]);
        var orderCeiling = useFundedOrderCeiling ? account.Mandate.CapitalAllocation : maximumOrderNotional is { } requestedCeiling
            ? Math.Min(account.Mandate.MaximumOrderNotional, requestedCeiling) : account.Mandate.MaximumOrderNotional;
        var openPositions = useFundedOrderCeiling
            ? Math.Max(account.Mandate.MaximumOpenPositions, symbols.Length)
            : account.Mandate.MaximumOpenPositions;
        if (!account.Mandate.AllowedSymbols.Order(StringComparer.Ordinal).SequenceEqual(symbols) ||
            account.Mandate.MaximumOrderNotional != orderCeiling ||
            account.Mandate.MaximumOpenPositions != openPositions)
        {
            // Recover broker-confirmed results before evaluating unresolved local orders.
            // A receipt/wallet failure still blocks; this never retries a placement or cancel.
            if (account.PerformanceBaseline is not null && account.Orders.Any(o => o.State is not
                (TradingCompany.Domain.LiveOrderState.Filled or TradingCompany.Domain.LiveOrderState.Cancelled or
                TradingCompany.Domain.LiveOrderState.Rejected)))
            {
                var recovered = await reconciliation.ReconcileAsync(configured.AccountId, token);
                if (!recovered.Complete && !recovered.Violations.SequenceEqual(new[] { "DAILY_BASELINE_REQUIRED" }))
                    return new(false, ["UNRESOLVED_ORDER", .. recovered.Violations]);
                account = await controls.ReadAsync(token);
                if (account.Gate.Stopped) return new(false, ["OWNER_STOP_ACTIVE"]);
            }
            if (account.Orders.Any(o => o.State is not (TradingCompany.Domain.LiveOrderState.Filled or
                TradingCompany.Domain.LiveOrderState.Cancelled or TradingCompany.Domain.LiveOrderState.Rejected)))
                return new(false, ["UNRESOLVED_ORDER"]);
            var expanded = await controls.SaveMandateAsync(account.Mandate with
            {
                Version = checked(account.Mandate.Version + 1), AllowedSymbols = symbols,
                MaximumOrderNotional = orderCeiling, MaximumOpenPositions = openPositions
            }, token);
            if (!expanded.Allowed) return new(false, expanded.Violations);
            account = await controls.ReadAsync(token);
        }
        var approvedMandate = account.Mandate.Version;
        // The complete two-read recovery, preflight and Arm share one account lease.
        // A queued background failure cannot invalidate evidence between these steps.
        var activated = await controls.ArmCurrentSnapshotForOwnerCommandAsync(configured.AccountId,
            configured.Verification.BrokerAccountReference, approvedMandate, reconciliation, token);
        return new(activated.Allowed, activated.Violations);
    }

    public static async Task<int> PrepareMarketsAsync()
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            { EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") });
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["LiveExecution:Enabled"] = "true" });
            BitkubLiveRuntime.Configure(builder.Services, builder.Configuration);
            using var host = builder.Build(); // No background service, ledger update or execution is started.
            var broker = host.Services.GetRequiredService<BitkubLiveBroker>();
            var configured = host.Services.GetRequiredService<BitkubConfiguredAccount>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (!await broker.CheckTradePermissionAsync(deadline.Token)) throw new InvalidDataException();
            var markets = await broker.GetAvailableMarketsAsync(deadline.Token);
            var v = configured.Verification;
            var previous = new BitkubVerifiedProfile(v.BrokerAccountReference, v.CredentialFingerprint, v.ConfirmationReference,
                v.EvidenceHash, v.AccountBindingVerified, v.TradePermissionVerified, v.Rules);
            var credentials = BitkubCredentialsFile.Read(builder.Configuration["Bitkub:CredentialsFile"]!);
            var fingerprint = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(credentials.ApiKey + "\0" + credentials.ApiSecret)));
            var expanded = BitkubProfileVerifier.ExpandReceiptMarkets(previous, markets, fingerprint, DateTimeOffset.UtcNow);
            var profile = "/profile-output/bitkub-verification.json";
            using var lease = new FileStream(profile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var before = await File.ReadAllBytesAsync(profile, deadline.Token);
            var current = System.Text.Json.JsonSerializer.Deserialize<BitkubVerifiedProfile>(before) ?? throw new InvalidDataException();
            if (System.Text.Json.JsonSerializer.Serialize(current) != System.Text.Json.JsonSerializer.Serialize(previous))
                throw new InvalidDataException();
            if (System.Text.Json.JsonSerializer.Serialize(expanded) != System.Text.Json.JsonSerializer.Serialize(previous))
            {
                var backup = profile + ".before-market-" + Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(before));
                if (File.Exists(backup) && !(await File.ReadAllBytesAsync(backup, deadline.Token)).SequenceEqual(before))
                    throw new InvalidDataException();
                if (!File.Exists(backup)) await File.WriteAllBytesAsync(backup, before, deadline.Token);
                var temporary = profile + ".pending-" + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(temporary, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(expanded), deadline.Token);
                    File.Move(temporary, profile, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            Console.WriteLine($"{markets.Count} active THB Spot markets verified. Existing account retained; no activation or order.");
            return 0;
        }
        catch { Console.WriteLine("Market expansion did not verify. No activation or order was sent."); return 1; }
    }

    public static async Task<int> RunCommandAsync(bool activate = true, decimal? maximumOrderNotional = null,
        bool useFundedOrderCeiling = false)
    {
        var phase = ActivationPhase.Configuration;
        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            { EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") });
            builder.Logging.ClearProviders();
            if (!activate) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["LiveExecution:Enabled"] = "true" });
            BitkubLiveRuntime.Configure(builder.Services, builder.Configuration);
            builder.Services.AddSingleton<LiveOwnerActivation>();
            using var host = builder.Build();
            if (host.Services.GetService<BitkubConfiguredAccount>() is null)
            { Console.WriteLine("LIVE_NOT_ACTIVATED: verified execution configuration is required."); return 1; }
            // A recovered order, mandate refresh and explicit new-day baseline each require
            // two complete bounded observations over the verified native market catalog.
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            if (!activate)
            {
                phase = ActivationPhase.Inspection;
                var broker = host.Services.GetRequiredService<BitkubLiveBroker>();
                if (!await broker.CheckTradePermissionAsync(deadline.Token)) throw new InvalidDataException();
                _ = await broker.GetWalletAsync(deadline.Token);
                Console.WriteLine("LIVE_CONFIGURATION_VERIFIED: profile, key binding and current Trade access passed. No activation or order.");
                return 0;
            }
            phase = ActivationPhase.Storage;
            var store = host.Services.GetRequiredService<LiveTradingStore>();
            await store.EnsureSchemaAsync(deadline.Token);
            if (host.Services.GetService<BitkubReceiptObserver>() is { } observer)
            {
                phase = ActivationPhase.ReceiptStream;
                await observer.StartAsync(deadline.Token);
                while (!observer.Ready) await Task.Delay(100, deadline.Token);
            }
            phase = ActivationPhase.Activation;
            var result = await host.Services.GetRequiredService<LiveOwnerActivation>().ActivateFullSpotAsync(deadline.Token,
                maximumOrderNotional, useFundedOrderCeiling);
            Console.WriteLine(result.Activated ? "LIVE_ACTIVATED: owner Spot mandate and broker reconciliation passed. No order was sent by this command." :
                "LIVE_NOT_ACTIVATED: " + string.Join(",", result.Reasons));
            return result.Activated ? 0 : 1;
        }
        catch (Exception error)
        { Console.WriteLine(CommandFailure(phase, error)); return 1; }
    }

    internal enum ActivationPhase { Configuration, Storage, ReceiptStream, Inspection, Activation }

    internal static string CommandFailure(ActivationPhase phase, Exception error)
    {
        // Raw exception messages may contain connection strings or authenticated URLs.
        // Emit only a code selected from types, plus the known command phase.
        var code = error switch
        {
            OperationCanceledException or TimeoutException => "TIMEOUT",
            Npgsql.NpgsqlException { InnerException: TimeoutException } => "STORAGE_TIMEOUT",
            Npgsql.NpgsqlException => "STORAGE_UNAVAILABLE",
            HttpRequestException or System.Net.WebSockets.WebSocketException => "BROKER_CONNECTION_UNAVAILABLE",
            InvalidDataException => "EVIDENCE_UNVERIFIED",
            _ => "UNEXPECTED_FAILURE"
        };
        return $"LIVE_NOT_ACTIVATED: {phase} / {code}. No order was sent by this command.";
    }
}
