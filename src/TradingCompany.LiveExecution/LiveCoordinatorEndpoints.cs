using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.LiveExecution;

public static class LiveCoordinatorEndpoints
{
    public static void MapCoordinatorEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/internal/live").RequireAuthorization("Coordinator");
        routes.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception) { return Results.Json(new { code = "LIVE_UNAVAILABLE" }, statusCode: 503); }
        });
        routes.MapGet("/research/{accountId:guid}", async (Guid accountId, HttpContext context, TimeProvider clock) =>
        {
            if (!Bound(context, accountId)) return Results.StatusCode(403);
            var controls = context.RequestServices.GetService<LiveOwnerControls>();
            if (controls is null) return Results.StatusCode(503);
            var account = await controls.ReadAsync(context.RequestAborted);
            if (account.AccountId.Value != accountId) return Results.StatusCode(403);
            var mandate = account.Mandate;
            var now = clock.GetUtcNow();
            var baseline = account.PerformanceBaseline;
            // Construct readiness explicitly rather than trusting a saved armed flag.
            var ready = account.Gate.Armed && !account.Gate.Stopped && account.Gate.IsolationVerified &&
                LiveRiskPolicy.IsValidMandate(mandate) && !(mandate.ExpiresAt <= now) &&
                account.Gate.ArmedMandateVersion == mandate.Version && account.Gate.CredentialFingerprint == mandate.CredentialFingerprint &&
                account.Snapshot.Reconciled && baseline is not null && baseline.DayStartEquity > 0 &&
                baseline.HighWaterEquity >= baseline.DayStartEquity &&
                baseline.BangkokDate == DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime) &&
                !account.Orders.Any(o => o.State is LiveOrderState.Reserved or LiveOrderState.Dispatching or LiveOrderState.Unknown ||
                    (o.CancellationRequestedAt is not null && o.State is not (LiveOrderState.Rejected or LiveOrderState.Filled or LiveOrderState.Cancelled) &&
                     o.CancellationOutcome != BrokerSubmissionState.Rejected));
            return Results.Ok(new LiveResearchPacket(new(TradingMode.Live, accountId.ToString("N")), mandate.Version,
                account.Snapshot, new(mandate.CapitalAllocation, mandate.MaximumPortfolioExposure,
                    mandate.MaximumOpenPositions, mandate.FeeReserveRate, mandate.MaximumSlippage, mandate.AllowedSymbols)
                {
                    OrderSizing = context.RequestServices.GetService<BitkubConfiguredAccount>()?.Verification.Rules.Pairs
                        .Select(p => new LiveOrderSizing(p.Symbol, p.PriceStep, p.SellBaseStep, p.BuyQuoteStep, p.MinimumQuoteSize)).ToArray() ?? []
                }, ready, account.Gate.Stopped));
        });
        routes.MapPost("/intents", async (LiveIntent intent, HttpContext context) =>
        {
            if (intent.Mode != TradingMode.Live || !Bound(context, intent.AccountId.Value)) return Results.StatusCode(403);
            var controls = context.RequestServices.GetService<LiveOwnerControls>();
            var execution = context.RequestServices.GetService<LiveExecutionService>();
            if (controls is null || execution is null) return Results.StatusCode(503);
            if ((await controls.ReadAsync(context.RequestAborted)).AccountId != intent.AccountId) return Results.StatusCode(403);
            var order = await execution.SubmitAsync(intent, context.RequestAborted);
            // No credentials, prepared wire body, raw audit or capability state leaves the executor.
            return Results.Ok(new LiveIntentReceipt(order.Intent, order.State, order.Violations));
        });
        routes.MapPost("/meetings", async (DailyMeetingAttempt attempt, HttpContext context, TimeProvider clock) =>
        {
            if (attempt.AccountContext?.Mode != TradingMode.Live || !Guid.TryParse(attempt.AccountContext.AccountId, out var account) || !Bound(context, account)) return Results.StatusCode(403);
            var store = context.RequestServices.GetService<ILiveTradingStore>();
            if (store is null) return Results.StatusCode(503);
            await store.WithAccountLockAsync(new(account), async (tx, token) =>
            {
                tx.RecordMeeting(attempt, clock.GetUtcNow());
                await tx.CommitAsync(token);
                return true;
            }, context.RequestAborted);
            return Results.Ok(new { saved = true });
        });
        routes.MapPost("/activity", (LiveCompanyActivity activity, HttpContext context, LiveActivityStore store) =>
        {
            var configured = context.RequestServices.GetService<BitkubConfiguredAccount>();
            if (configured is null) return Results.StatusCode(503);
            if (activity.AccountContext?.Mode != TradingMode.Live ||
                activity.AccountContext.AccountId != configured.AccountId.Value.ToString("N") ||
                !Bound(context, configured.AccountId.Value)) return Results.StatusCode(403);
            return store.Save(activity, configured.AccountId) ? Results.Ok(new { saved = true }) : Results.BadRequest();
        });
    }

    private static bool Bound(HttpContext context, Guid accountId) => accountId != Guid.Empty &&
        context.User.FindFirst("live-account")?.Value == accountId.ToString("N");
}
