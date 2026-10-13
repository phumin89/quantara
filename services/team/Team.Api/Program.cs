using System.Text.Json;
using System.Text.Json.Serialization;
using Mediator;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

if (args.FirstOrDefault() == "exec")
{
    Environment.ExitCode = await CodexLauncher.Run(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5052");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 12000);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var taskDirectory = new DirectoryInfo(builder.Environment.ContentRootPath);
while (taskDirectory is not null && !File.Exists(Path.Combine(taskDirectory.FullName, "Quantara.sln")))
{
    taskDirectory = taskDirectory.Parent;
}

var root = taskDirectory?.FullName ?? builder.Environment.ContentRootPath;
var configuredConnection = builder.Configuration.GetConnectionString("Team");
var teamConnection =
    configuredConnection
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=QuantaraTeam;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;";
var store = new TeamRunStore(teamConnection, configuredConnection is null);
builder.Services.AddSingleton<ITeamRunStore>(store);
builder.Services.AddSingleton<IReadOnlyTeamRunStore>(store);
builder.Services.AddSingleton<ICompanyScheduleStore>(new CompanyScheduleStore(teamConnection));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICodexWorker>(
    new CodexWorker(
        NodeRuntime.Resolve(builder.Configuration["Codex:NodePath"]),
        Path.Combine(root, "services", "ai-worker", "run.mjs"),
        builder.Configuration["Codex:AuthHome"]
            ?? Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Team.Api.exe" : "Team.Api")
    )
);
var portfolioUrl = new Uri(builder.Configuration["Services:PortfolioUrl"] ?? "http://127.0.0.1:5050");
if (portfolioUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(portfolioUrl.UserInfo))
{
    throw new InvalidOperationException("Configure a Portfolio URL without embedded credentials.");
}

builder
    .Services.AddHttpClient<IPortfolioSnapshotReader, PortfolioSnapshotReader>(http =>
    {
        http.BaseAddress = portfolioUrl;
        http.Timeout = TimeSpan.FromSeconds(15);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<TeamCoordinator>();
builder
    .Services.AddHttpClient<ICurrentAccountReader, CurrentAccountReader>(http =>
    {
        http.BaseAddress = portfolioUrl;
        http.Timeout = TimeSpan.FromSeconds(5);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder
    .Services.AddHttpClient<IOrderProposalPublisher, OrderProposalPublisher>(http =>
    {
        http.BaseAddress = portfolioUrl;
        http.Timeout = TimeSpan.FromSeconds(20);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder
    .Services.AddHttpClient<IMarketResearchReader, MarketResearchReader>(http =>
    {
        http.BaseAddress = new Uri(builder.Configuration["Services:MarketDataUrl"] ?? "http://127.0.0.1:5051");
        http.Timeout = TimeSpan.FromSeconds(20);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<ITeamCoordinator>(services => services.GetRequiredService<TeamCoordinator>());
builder.Services.AddHostedService(services => services.GetRequiredService<TeamCoordinator>());
builder.Services.AddSingleton<CompanyLoop>();
builder.Services.AddSingleton<ICompanyLoop>(services => services.GetRequiredService<CompanyLoop>());
builder.Services.AddSingleton<IReadOnlyCompanyLoop>(services => services.GetRequiredService<CompanyLoop>());
builder.Services.AddHostedService(services => services.GetRequiredService<CompanyLoop>());
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter<TeamRole>(JsonNamingPolicy.CamelCase, false)
        );
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter<TeamRunStatus>(JsonNamingPolicy.CamelCase, false)
        );
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter<CompanyLoopStatus>(JsonNamingPolicy.CamelCase, false)
        );
    });
var app = builder.Build();
app.Use(
    async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        if (
            context.Request.Host.Host is not ("127.0.0.1" or "localhost" or "::1")
            || context.Request.Headers.ContainsKey("Origin")
        )
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Use the application to access the team service." });
            return;
        }

        try
        {
            await next();
        }
        catch (TeamBusyException error)
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
        }
        catch (TeamUnavailableException error)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
        }
        catch (ArgumentException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Enter an objective of 1–2000 characters." });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(
                new { error = "AI team storage is unavailable. Check SQL Server and retry." }
            );
        }
    }
);
app.MapControllers();
await app.RunAsync();
