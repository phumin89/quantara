using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;

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
        // Read back the bounded display projection for operational diagnosis only.
        routes.MapGet("/activity/{accountId:guid}", (Guid accountId, HttpContext context, LiveActivityStore store) =>
            !Bound(context, accountId) ? Results.StatusCode(403) :
                Results.Ok(new { activity = store.Read(new(accountId)) }));
        routes.MapGet("/research/{accountId:guid}", async (Guid accountId, HttpContext context, TimeProvider clock) =>
        {
            if (!Bound(context, accountId)) return Results.StatusCode(403);
            var controls = context.RequestServices.GetService<LiveOwnerControls>();
            if (controls is null) return Results.StatusCode(503);
            var account = await controls.ReadAsync(context.RequestAborted);
            if (account.AccountId.Value != accountId) return Results.StatusCode(403);
            var mandate = account.Mandate;
            var portfolioLimits = LiveRiskPolicy.PortfolioLimits(mandate, account.Snapshot);
            var now = clock.GetUtcNow();
            var baseline = account.PerformanceBaseline;
            var broker = context.RequestServices.GetService<BitkubLiveBroker>();
            var configured = context.RequestServices.GetService<BitkubConfiguredAccount>();
            string[] researchSymbols = configured is null ? mandate.AllowedSymbols.ToArray() : configured.Verification.Rules.Pairs.Select(p => p.Symbol).ToArray();
            var researchQuotes = account.Snapshot.Quotes.Where(q => researchSymbols.Contains(q.Symbol) &&
                q.ObservedAt <= now && now - q.ObservedAt <= TimeSpan.FromSeconds(30)).ToList();
            if (broker is not null && researchSymbols.Length > 3)
                researchQuotes = (await broker.GetQuotesAsync(researchSymbols, context.RequestAborted)).ToList();
            else if (broker is not null)
                foreach (var symbol in researchSymbols.Where(s => researchQuotes.All(q => q.Symbol != s)))
                    researchQuotes.Add(await broker.GetQuoteAsync(symbol, context.RequestAborted));
            // Construct readiness explicitly rather than trusting a saved armed flag.
            var ready = account.Gate.Armed && !account.Gate.Stopped && account.Gate.IsolationVerified &&
                (context.RequestServices.GetService<BitkubReceiptObserver>() is not { } observer || observer.Ready) &&
                LiveRiskPolicy.IsValidMandate(mandate) && !(mandate.ExpiresAt <= now) &&
                account.Gate.ArmedMandateVersion == mandate.Version && account.Gate.CredentialFingerprint == mandate.CredentialFingerprint &&
                account.Snapshot.Reconciled && baseline is not null && baseline.DayStartEquity > 0 &&
                baseline.HighWaterEquity >= baseline.DayStartEquity &&
                baseline.BangkokDate == DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime) &&
                !account.Orders.Any(o => o.State is LiveOrderState.Reserved or LiveOrderState.Dispatching or LiveOrderState.Unknown ||
                    (o.CancellationRequestedAt is not null && o.State is not (LiveOrderState.Rejected or LiveOrderState.Filled or LiveOrderState.Cancelled) &&
                     o.CancellationOutcome != BrokerSubmissionState.Rejected));
            return Results.Ok(new LiveResearchPacket(new(TradingMode.Live, accountId.ToString("N")), mandate.Version,
                account.Snapshot, new(portfolioLimits.CapitalAllocation, mandate.MaximumPortfolioExposure,
                    mandate.MaximumOpenPositions, mandate.FeeReserveRate, mandate.MaximumSlippage, mandate.AllowedSymbols)
                {
                    MaximumOrderNotional = portfolioLimits.MaximumOrderNotional,
                    MaximumDailyTurnover = mandate.UseFullPortfolio ? null : mandate.MaximumDailyTurnover,
                    UseFullPortfolio = mandate.UseFullPortfolio,
                    ResearchSymbols = researchSymbols,
                    OrderSizing = context.RequestServices.GetService<BitkubConfiguredAccount>()?.Verification.Rules.Pairs
                        .Where(p => mandate.AllowedSymbols.Contains(p.Symbol))
                        .Select(p => new LiveOrderSizing(p.Symbol, p.PriceStep, p.SellBaseStep, p.BuyQuoteStep, p.MinimumQuoteSize)).ToArray() ?? []
                }, ready, account.Gate.Stopped) { ResearchQuotes = researchQuotes, MinimumReviewIntervalMinutes = 15,
                    TradePlans = LiveTradePlans.Views(account, now, context.RequestServices.GetService<BitkubConfiguredAccount>()?.Verification.Rules.Pairs
                        .Select(p => new LiveOrderSizing(p.Symbol, p.PriceStep, p.SellBaseStep, p.BuyQuoteStep, p.MinimumQuoteSize)).ToArray())
                        .Where(LiveTradePlans.Active).ToArray() });
        });
        routes.MapPost("/plans", async (LiveTradePlanRequest plan, HttpContext context) =>
        {
            if (!Bound(context, plan.AccountId.Value)) return Results.StatusCode(403);
            var configured = context.RequestServices.GetService<BitkubConfiguredAccount>();
            var plans = context.RequestServices.GetService<LiveTradePlanService>();
            if (configured is null || plans is null) return Results.StatusCode(503);
            if (configured.AccountId != plan.AccountId) return Results.StatusCode(403);
            return Results.Ok(await plans.RegisterAsync(plan, context.RequestAborted));
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
