using Microsoft.Extensions.Options;
using TradingCompany.Application;
using TradingCompany.Api;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using TradingCompany.AgentRuntime;

var builder = WebApplication.CreateBuilder(args);
var webOrigin = builder.Configuration["WebOrigin"] ?? "http://localhost:3000";
var webOrigins = webOrigin is "http://localhost:3000" or "http://127.0.0.1:3000"
    ? new[] { "http://localhost:3000", "http://127.0.0.1:3000" }
    : new[] { webOrigin };
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(webOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeEventPublisher, SignalRRealtimeEventPublisher>();
builder.Services.AddHttpClient("market-data", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<IUsdThbRateProvider, FrankfurterUsdThbRateProvider>();
builder.Services.AddSingleton<IHistoricalMarketDataProvider, RealHistoricalMarketDataProvider>();
builder.Services.AddSingleton<RiskEngine>();
builder.Services.AddOptions<AiCompanySettings>()
    .Bind(builder.Configuration.GetSection("TradingAgents:Company"));
builder.Services.AddHttpClient("trading-agent", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com");
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddSingleton<ITradingAgentGateway>(sp =>
    (builder.Configuration["TradingAgents:Company:Gateway"] ?? "openai-api").ToLowerInvariant() switch
    {
        "codex-cli" => new CodexCliTradingAgentGateway(builder.Configuration["TradingAgents:Company:CodexExecutable"] ?? "codex"),
        "openai-api" => new OpenAiTradingAgentGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("trading-agent"), openAiApiKey),
        _ => throw new InvalidOperationException("Unknown trading-agent gateway. No provider fallback was selected.")
    });
builder.Services.AddSingleton<IAgentDecisionArtifactStore>(sp =>
{
    var configured = builder.Configuration["TradingAgents:Company:ArtifactPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "agent-artifacts");
    var path = Path.IsPathFullyQualified(configured)
        ? configured
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configured));
    return new JsonAgentDecisionArtifactStore(path);
});
builder.Services.AddSingleton<IAiCompanyMeetingRunner>(sp =>
{
    var configured = sp.GetRequiredService<IOptions<AiCompanySettings>>().Value;
    return new AiCompanyMeetingRunner(
        sp.GetRequiredService<ITradingAgentGateway>(),
        sp.GetRequiredService<IAgentDecisionArtifactStore>(),
        new AiCompanyRuntimeOptions(
            configured.Enabled,
            configured.Model,
            configured.ReasoningEffort,
            configured.PromptVersion,
            configured.MandateVersion,
            configured.MaxOutputTokens,
            configured.MaxModelCallsPerMeeting,
            configured.MaxTotalTokensPerMeeting,
            TimeSpan.FromSeconds(configured.TimeoutSeconds)), events: sp.GetRequiredService<IRealtimeEventPublisher>());
});
builder.Services.AddSingleton(TimeProvider.System);
var hostProxyAddress = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" &&
    System.Net.IPAddress.TryParse(builder.Configuration["SandboxControl:HostProxyAddress"], out var configuredHostProxy)
        ? configuredHostProxy : null;
builder.Services.AddSingleton(_ => new SandboxControl(
    (builder.Configuration["ForwardFunding:StatePath"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "data", "forward-funding-v1.json"))) + ".control.json", webOrigin, hostProxyAddress));
builder.Services.AddSingleton<IForwardFundingStore>(_ => new JsonForwardFundingStore(
    builder.Configuration["ForwardFunding:StatePath"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "data", "forward-funding-v1.json"))));
builder.Services.AddSingleton<IForwardInvestmentStore>(_ => new JsonForwardInvestmentStore(
    (builder.Configuration["ForwardFunding:StatePath"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "data", "forward-funding-v1.json"))) + ".investment.json"));
builder.Services.AddSingleton<ForwardFundingService>();
builder.Services.AddSingleton<JsonForwardPaperStore>(_ => new JsonForwardPaperStore(
    (builder.Configuration["ForwardFunding:StatePath"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "data", "forward-funding-v1.json"))) + ".paper-v2.json"));
builder.Services.AddHostedService(sp => sp.GetRequiredService<ForwardFundingService>());
builder.Services.AddSingleton<AlpacaForwardPaperQuotes>();
builder.Services.AddSingleton<BitkubForwardPaperQuotes>();
builder.Services.AddSingleton<IForwardPaperQuotes, ForwardMarketQuotes>();
builder.Services.AddSingleton<ForwardPaperCompanyService>(sp => new(
    sp.GetRequiredService<IForwardFundingStore>(), sp.GetRequiredService<IForwardInvestmentStore>(),
    sp.GetRequiredService<JsonForwardPaperStore>(), sp.GetRequiredService<IAiCompanyMeetingRunner>(),
    sp.GetRequiredService<IHistoricalMarketDataProvider>(), sp.GetRequiredService<IForwardPaperQuotes>(),
    new JsonDailyMeetingJournal((builder.Configuration["ForwardFunding:StatePath"] ??
        Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "data", "forward-funding-v1.json"))) + ".paper-meetings.json"),
    sp.GetRequiredService<TimeProvider>(), builder.Configuration,
    sp.GetRequiredService<ILogger<ForwardPaperCompanyService>>(), sp.GetRequiredService<SandboxControl>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<ForwardPaperCompanyService>());
builder.Services.AddSingleton<CompanyConversationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CompanyConversationService>());
var app = builder.Build();
app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "paper-only" }));
app.MapGet("/forward-funding/status", (ForwardFundingService funding) => funding.GetStatus());
app.MapGet("/forward-company/status", (ForwardPaperCompanyService company) => Results.Ok(company.GetStatus()));
app.MapGet("/sandbox/conversations", async (HttpContext context, CompanyConversationService reports, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try { var result = await reports.DatesAsync(cancellationToken); return result is null ? Results.StatusCode(503) : Results.Ok(result); }
    catch (Exception ex) when (ex is Npgsql.NpgsqlException or IOException or InvalidDataException) { return Results.StatusCode(503); }
});
app.MapGet("/sandbox/conversations/{date}", async (string date, HttpContext context, CompanyConversationService reports, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)) return Results.BadRequest();
    try { var result = await reports.ReadAsync(day, cancellationToken); return result is null ? Results.StatusCode(503) : Results.Ok(result); }
    catch (Exception ex) when (ex is Npgsql.NpgsqlException or IOException or InvalidDataException) { return Results.StatusCode(503); }
});
app.MapHub<TradingHub>("/realtime/trading");
app.MapGet("/sandbox/control", (HttpContext context, SandboxControl control) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return control.Trusted(context) ? Results.Ok(new { token = control.Token }) : Results.StatusCode(403);
});
app.MapPost("/sandbox/control", (HttpContext context, SandboxControl control, ForwardPaperCompanyService company, SandboxPauseRequest request) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!control.Trusted(context, mutation: true)) return Results.StatusCode(403);
    if (request.Paused is null) return Results.BadRequest();
    return company.SetPaused(request.Paused.Value) ? Results.Ok(new { paused = request.Paused.Value }) : Results.Conflict(new { error = "Inspection required before changing Sandbox control." });
});
app.Run();
public partial class Program;
