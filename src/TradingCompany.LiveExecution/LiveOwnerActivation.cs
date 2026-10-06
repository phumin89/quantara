using TradingCompany.Application;
using TradingCompany.Infrastructure;

namespace TradingCompany.LiveExecution;

public sealed record LiveOwnerActivationResult(bool Activated, IReadOnlyList<string> Reasons);

// Explicit local owner command only. No API/model caller receives this capability.
// Uses the existing owner controls and account lock; never places or cancels an order.
public sealed class LiveOwnerActivation(BitkubConfiguredAccount configured, LiveTradingStore store,
    BitkubLiveBroker broker, BitkubAccountProvisioner provisioner, LiveReconciliation reconciliation,
    LiveOwnerControls controls)
{
    public async Task<LiveOwnerActivationResult> ActivateFullSpotAsync(CancellationToken token)
    {
        if (!await broker.CheckTradePermissionAsync(token)) return new(false, ["TRADE_PERMISSION_REQUIRED"]);
        if (!await store.ExistsAsync(configured.AccountId, token))
        {
            var provisioned = await provisioner.ProvisionFullSpotAsync(token);
            if (!provisioned.Allowed) return new(false, provisioned.Violations);
        }
        var account = await controls.ReadAsync(token);
        if (account.Gate.Stopped) return new(false, ["OWNER_STOP_ACTIVE"]);
        var approvedMandate = account.Mandate.Version;
        if (account.PerformanceBaseline is null)
        {
            var baseline = await reconciliation.InitializeBaselineAsync(new(configured.AccountId,
                account.Mandate.Version, account.Snapshot.Version), token);
            if (!baseline.Complete) return new(false, baseline.Violations);
        }
        else
        {
            var refreshed = await reconciliation.ReconcileAsync(configured.AccountId, token);
            if (!refreshed.Complete) return new(false, refreshed.Violations);
        }
        account = await controls.ReadAsync(token);
        if (account.Mandate.Version != approvedMandate) return new(false, ["STALE_APPROVAL"]);
        var activated = await controls.ArmAsync(new(configured.AccountId, configured.Verification.BrokerAccountReference,
            account.Mandate.Version, account.Snapshot.Version, true, false), token);
        // Reconciliation can update only the snapshot between read and arm. Retry
        // once without accepting a changed mandate or clearing an owner stop.
        if (!activated.Allowed && activated.Violations.SequenceEqual(new[] { "STALE_APPROVAL" }))
        {
            account = await controls.ReadAsync(token);
            if (account.Mandate.Version != approvedMandate || account.Gate.Stopped) return new(false, activated.Violations);
            activated = await controls.ArmAsync(new(configured.AccountId, configured.Verification.BrokerAccountReference,
                approvedMandate, account.Snapshot.Version, true, false), token);
        }
        return new(activated.Allowed, activated.Violations);
    }

    public static async Task<int> RunCommandAsync(bool activate = true)
    {
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
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            if (!activate)
            {
                var broker = host.Services.GetRequiredService<BitkubLiveBroker>();
                if (!await broker.CheckTradePermissionAsync(deadline.Token)) throw new InvalidDataException();
                _ = await broker.GetWalletAsync(deadline.Token);
                Console.WriteLine("LIVE_CONFIGURATION_VERIFIED: profile, key binding and current Trade access passed. No activation or order.");
                return 0;
            }
            var store = host.Services.GetRequiredService<LiveTradingStore>();
            await store.EnsureSchemaAsync(deadline.Token);
            if (host.Services.GetService<BitkubReceiptObserver>() is { } observer)
            {
                await observer.StartAsync(deadline.Token);
                while (!observer.Ready) await Task.Delay(100, deadline.Token);
            }
            var result = await host.Services.GetRequiredService<LiveOwnerActivation>().ActivateFullSpotAsync(deadline.Token);
            Console.WriteLine(result.Activated ? "LIVE_ACTIVATED: owner Spot mandate and broker reconciliation passed. No order was sent by this command." :
                "LIVE_NOT_ACTIVATED: " + string.Join(",", result.Reasons));
            return result.Activated ? 0 : 1;
        }
        catch
        { Console.WriteLine("LIVE_NOT_ACTIVATED: account, storage or broker verification failed. No order was sent by this command."); return 1; }
    }
}
