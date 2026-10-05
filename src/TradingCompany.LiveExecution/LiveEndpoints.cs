using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.LiveExecution;

public static class LiveEndpoints
{
    public static void MapLiveEndpoints(this WebApplication app)
    {
        var live = app.MapGroup("/live").RequireAuthorization("OwnerRead");
        // Only this group can mutate state; remembered viewing cannot satisfy its policy.
        var ownerControls = app.MapGroup("/live").RequireAuthorization("Owner");
        live.AddEndpointFilter(SafeResult);
        ownerControls.AddEndpointFilter(SafeResult);
        live.MapGet("/status", async (HttpContext context, TimeProvider clock) =>
        {
            var controls = Controls(context);
            if (controls is null) return Results.Ok(new { state = "DISABLED", armed = false, reasons = new[] { "EXECUTOR_NOT_CONFIGURED" } });
            var account = await controls.ReadAsync(context.RequestAborted);
            var now = clock.GetUtcNow();
            var reasons = new List<string>();
            if (!account.Gate.IsolationVerified) reasons.Add("ISOLATION_REQUIRED");
            if (!LiveRiskPolicy.IsValidMandate(account.Mandate)) reasons.Add("INVALID_MANDATE");
            if (account.Mandate.ExpiresAt <= now) reasons.Add("MANDATE_EXPIRED");
            if (account.Mandate.CredentialFingerprint != account.Gate.CredentialFingerprint) reasons.Add("ACCOUNT_BINDING_CHANGED");
            if (account.Gate.Armed && account.Gate.ArmedMandateVersion != account.Mandate.Version) reasons.Add("STALE_APPROVAL");
            if (account.PerformanceBaseline is not { } baseline || baseline.DayStartEquity <= 0 ||
                baseline.HighWaterEquity < baseline.DayStartEquity ||
                baseline.BangkokDate != DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime))
                reasons.Add("DAILY_BASELINE_REQUIRED");
            if (account.Orders.Any(o => o.State is LiveOrderState.Reserved or LiveOrderState.Dispatching or LiveOrderState.Unknown ||
                (o.CancellationRequestedAt is not null && o.State is not (LiveOrderState.Rejected or LiveOrderState.Filled or LiveOrderState.Cancelled) &&
                 o.CancellationOutcome != BrokerSubmissionState.Rejected))) reasons.Add("UNRESOLVED_ORDER");
            if (!account.Snapshot.Reconciled) reasons.Add("RECONCILIATION_REQUIRED");
            if (account.Snapshot.ObservedAt > now || now - account.Snapshot.ObservedAt > TimeSpan.FromSeconds(30)) reasons.Add("STALE_SNAPSHOT");
            var state = account.Gate.Stopped ? "STOPPED" : reasons.Count > 0 ? "BLOCKED" : account.Gate.Armed ? "ARMED" : "DISARMED";
            return Results.Ok(new { state, armed = state == "ARMED", accountId = account.AccountId,
                mandateVersion = account.Mandate.Version, snapshotVersion = account.Snapshot.Version,
                observedAt = account.Snapshot.ObservedAt, reasons });
        });
        live.MapGet("/account", async (HttpContext context) =>
        {
            var controls = Controls(context);
            if (controls is null) return Unconfigured();
            var account = await controls.ReadAsync(context.RequestAborted);
            var m = account.Mandate;
            return Results.Ok(new
            {
                accountId = account.AccountId, brokerAccountReference = account.BrokerAccountReference,
                snapshot = account.Snapshot,
                mandate = new { m.Version, m.CapitalAllocation, m.MaximumOrderNotional, m.MaximumDailyTurnover,
                    m.MaximumDailyLoss, m.MaximumDrawdown, m.MaximumPortfolioExposure, m.MaximumOpenPositions,
                    m.MaximumSlippage, m.FeeReserveRate, m.AllowedSymbols, m.ExpiresAt },
                orders = account.Orders.Select(o => new { intentId = o.Intent.Id, symbol = o.Intent.Symbol,
                    side = o.Intent.Side.ToString(), state = o.State.ToString(), o.Intent.Quantity, o.FilledQuantity,
                    o.ReservedCash, o.ReservedQuantity, o.CancellationRequestedAt,
                    cancellationOutcome = o.CancellationOutcome?.ToString(), o.UpdatedAt })
            });
        });
        live.MapGet("/wallet", async (HttpContext context) =>
        {
            var reader = context.RequestServices.GetService<TradingCompany.Infrastructure.BitkubWalletReader>();
            if (reader is null) return Unconfigured();
            return Results.Ok(await reader.ReadAsync(context.RequestAborted));
        });
        ownerControls.MapPost("/preflight", async (HttpContext context) =>
        {
            var controls = Controls(context);
            if (controls is null) return Unconfigured();
            var result = await controls.PreflightAsync(context.RequestAborted);
            return Results.Json(result, statusCode: result.Ready ? 200 : 409);
        });
        ownerControls.MapPost("/baseline", async (LiveBaselineRequest request, HttpContext context) =>
        {
            var controls = Controls(context);
            var reconciliation = context.RequestServices.GetService<LiveReconciliation>();
            if (controls is null || reconciliation is null) return Unconfigured();
            var account = await controls.ReadAsync(context.RequestAborted);
            if (request.AccountId != account.AccountId) return Results.Conflict(new { code = "ACCOUNT_MISMATCH" });
            var result = await reconciliation.InitializeBaselineAsync(request, context.RequestAborted);
            return Results.Json(result, statusCode: result.Complete ? 200 : 409);
        });
        ownerControls.MapPost("/arm", async (LiveArmRequest request, HttpContext context) =>
        {
            var controls = Controls(context);
            if (controls is null) return Unconfigured();
            var result = await controls.ArmAsync(request, context.RequestAborted);
            return Results.Json(result, statusCode: result.Allowed ? 200 : 409);
        });
        ownerControls.MapPost("/mandate", async (LiveMandateUpdate request, HttpContext context) =>
        {
            var controls = Controls(context);
            if (controls is null) return Unconfigured();
            var result = await controls.SaveMandateAsync(request, context.RequestAborted);
            return Results.Json(result, statusCode: result.Allowed ? 200 : 409);
        });
        ownerControls.MapPost("/stop", async (HttpContext context) =>
        {
            var controls = Controls(context);
            if (controls is null) return Unconfigured();
            await controls.StopAsync(context.RequestAborted);
            return Results.NoContent();
        });
        ownerControls.MapPost("/orders/{intentId:guid}/cancel", async (Guid intentId, HttpContext context) =>
        {
            var controls = Controls(context);
            var execution = context.RequestServices.GetService<LiveExecutionService>();
            if (controls is null || execution is null) return Unconfigured();
            var account = await controls.ReadAsync(context.RequestAborted);
            if (!account.Orders.Any(o => o.Intent.Id == intentId)) return Results.NotFound();
            var order = await execution.CancelOwnedAsync(account.AccountId, intentId, context.RequestAborted);
            return Results.Ok(new { intentId, state = order.State.ToString(),
                cancellationOutcome = order.CancellationOutcome?.ToString(), order.ReservedCash, order.ReservedQuantity });
        });
    }

    private static async ValueTask<object?> SafeResult(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (Exception)
        {
            // Never serialize provider exceptions, connection strings or raw account evidence.
            return Results.Json(new { code = "LIVE_UNAVAILABLE" }, statusCode: 503);
        }
    }

    private static LiveOwnerControls? Controls(HttpContext context) => context.RequestServices.GetService<LiveOwnerControls>();
    private static IResult Unconfigured() => Results.Json(new { code = "LIVE_ACCOUNT_NOT_CONFIGURED" }, statusCode: 503);
}
