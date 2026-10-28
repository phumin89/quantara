using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Hangfire.SqlServer;
using Mediator;
using Quantara.Portfolio.Api;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Infrastructure;
using Quantara.Portfolio.Infrastructure.Risk;
using Quantara.Portfolio.Infrastructure.Trading.Jobs;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Hosting:ListenUrl"] ?? "http://127.0.0.1:5050");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
const long bitkubMaximumResponseBytes = 32 * 1024 * 1024;
var taskDirectory = new DirectoryInfo(builder.Environment.ContentRootPath);
while (taskDirectory is not null && !File.Exists(Path.Combine(taskDirectory.FullName, "Quantara.sln")))
{
    taskDirectory = taskDirectory.Parent;
}

var environmentFile =
    builder.Configuration["Credentials:EnvironmentFile"]
    ?? Path.Combine(taskDirectory?.FullName ?? builder.Environment.ContentRootPath, ".env");
var settings = new Dictionary<string, string>();
if (File.Exists(environmentFile))
{
    foreach (var line in File.ReadLines(environmentFile))
    {
        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            continue;
        }

        var name = line[..separator].Trim();
        if (
            name
            is not (
                "BITKUB_API_KEY"
                or "BITKUB_API_SECRET"
                or "TELEGRAM_BOT_TOKEN"
                or "TELEGRAM_USER_ID"
                or "TELEGRAM_CHAT_ID"
            )
        )
        {
            continue;
        }

        settings[name] = line[(separator + 1)..].Trim().Trim('"', '\'');
    }
}

string Credential(string name) => Environment.GetEnvironmentVariable(name) ?? settings.GetValueOrDefault(name, "");
var credentials = new BitkubCredentials(Credential("BITKUB_API_KEY"), Credential("BITKUB_API_SECRET"));
var accountKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.Key))).ToLowerInvariant();
var configuredConnection = builder.Configuration.GetConnectionString("Quantara");
var connectionString =
    configuredConnection
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=Quantara;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;";
var store = new SqlPortfolioRepository(connectionString, accountKey);
await store.Initialize(configuredConnection is null, CancellationToken.None);
var orders = new SqlOrderStore(connectionString, accountKey);
builder.Services.AddSingleton<IAccountContext>(new AccountContext(accountKey));
await orders.Initialize(CancellationToken.None);
var riskStore = new SqlRiskStore(connectionString, accountKey);
await riskStore.Initialize(CancellationToken.None);
builder.Services.AddSingleton<IRiskStore>(riskStore);
builder.Services.AddSingleton<IReadOnlyRiskStore>(riskStore);
builder.Services.AddSingleton<RiskCapitalReader>();
builder.Services.AddSingleton<AutonomousRiskService>();
builder.Services.AddScoped<IRiskSizingService, RiskSizingService>();
builder.Services.AddSingleton<IOrderStore>(orders);
builder.Services.AddSingleton<IReadOnlyOrderStore>(orders);
builder.Services.AddHostedService<OrderStatusMonitor>();
builder.Services.AddSingleton(TimeProvider.System);
var tradingStore = new SqlTradingStore(connectionString, accountKey, TimeProvider.System);
await tradingStore.Initialize(CancellationToken.None);
builder.Services.AddSingleton<ITradingStore>(tradingStore);
builder.Services.AddSingleton<IReadOnlyTradingStore>(tradingStore);
var manualSaleAllocations = new SqlManualSaleAllocationStore(connectionString, accountKey);
await manualSaleAllocations.Initialize(CancellationToken.None);
builder.Services.AddSingleton<IManualSaleAllocationStore>(manualSaleAllocations);
var goalStore = new SqlPortfolioGoalStore(connectionString, accountKey);
await goalStore.Initialize(CancellationToken.None);
builder.Services.AddSingleton<IPortfolioGoalStore>(goalStore);
builder.Services.AddSingleton<IReadOnlyPortfolioGoalStore>(goalStore);
var tradingOptions = new TradingControlOptions(
    builder.Configuration["Trading:OwnerToken"] ?? Credential("QUANTARA_TRADING_CONTROL_TOKEN")
)
{
    LiveActivationAvailable =
        !string.IsNullOrWhiteSpace(credentials.Key) && !string.IsNullOrWhiteSpace(credentials.Secret),
};
builder.Services.AddSingleton(tradingOptions);
builder.Services.AddSingleton<ManualSaleReviewProof>();
builder.Services.AddScoped<ManualSaleReconciliation>(services => new ManualSaleReconciliation(
    accountKey,
    services.GetRequiredService<IReadOnlyTradingStore>(),
    services.GetRequiredService<IReadOnlyPortfolioRepository>(),
    services.GetRequiredService<IManualSaleAllocationStore>(),
    services.GetRequiredService<TimeProvider>()
));
builder.Services.AddScoped<IManualSaleReview, ManualSaleReviewService>();
builder.Services.AddScoped<IReadOnlyManualSaleReview>(services => services.GetRequiredService<IManualSaleReview>());
builder.Services.AddScoped<ICashBaselineReview, CashBaselineReviewService>();
builder.Services.AddScoped<IReadOnlyCashBaselineReview>(services => services.GetRequiredService<ICashBaselineReview>());
builder.Services.AddSingleton<TradingOverviewProjection>();
builder.Services.AddSingleton<TradingEventDispatcher>();
builder.Services.AddSingleton(new SemaphoreSlim(1, 1));
builder.Services.AddSingleton<ITradingControl, TradingControl>();
builder.Services.AddSingleton<TradingEngine>();
builder.Services.AddSingleton<AutonomousMarketMonitor>();
builder.Services.AddSingleton<TradingReportDelivery>();
builder.Services.AddHostedService<TradingWorker>();
builder.Services.AddSingleton<TradingEvaluationService>();
builder.Services.AddHostedService<TradingEvaluationWorker>();
builder.Services.AddSingleton<ITradingPolicyEvaluator, TradingPolicyEvaluator>();
builder.Services.AddSingleton<TradingPolicyService>();
builder.Services.AddHostedService<TradingPolicyWorker>();
builder
    .Services.AddHttpClient<ITradingPolicyCandidateReader, TeamPolicyCandidateReader>(http =>
    {
        http.BaseAddress = new Uri(builder.Configuration["Services:TeamUrl"] ?? "http://localhost:5052");
        http.Timeout = TimeSpan.FromSeconds(15);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder
    .Services.AddHttpClient<ITradingNotifier>(
        (services, http) =>
        {
            http.BaseAddress = new Uri("https://api.telegram.org/");
            http.Timeout = TimeSpan.FromSeconds(15);
        }
    )
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .AddTypedClient<ITradingNotifier>(
        (http, services) => new TradingNotifier(http, services.GetService<TelegramSettings>())
    );
TelegramSettings? tradingTelegram = null;
if (!string.IsNullOrWhiteSpace(Credential("TELEGRAM_BOT_TOKEN")))
{
    if (
        !long.TryParse(Credential("TELEGRAM_USER_ID"), out var telegramUserId)
        || !long.TryParse(Credential("TELEGRAM_CHAT_ID"), out var telegramChatId)
        || telegramUserId <= 0
        || telegramChatId <= 0
        || telegramUserId != telegramChatId
    )
    {
        throw new InvalidOperationException("Configure Telegram owner/private chat IDs before enabling order reviews.");
    }

    var telegramSettings = new TelegramSettings(Credential("TELEGRAM_BOT_TOKEN"), telegramUserId, telegramChatId);
    tradingTelegram = telegramSettings;
    var notifications = new SqlOrderNotificationStore(connectionString, accountKey);
    await notifications.Initialize(CancellationToken.None);
    builder.Services.AddSingleton(telegramSettings);
    builder.Services.AddSingleton<IOrderNotificationStore>(notifications);
    builder.Services.AddScoped<IHumanOrderReview, MediatorHumanOrderReview>();
    builder
        .Services.AddHttpClient<ITelegramTransport, TelegramTransport>(http =>
        {
            http.BaseAddress = new Uri("https://api.telegram.org/");
            http.Timeout = TimeSpan.FromSeconds(15);
        })
        .RemoveAllLoggers()
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddSingleton<TelegramOrderReviews>();
    builder.Services.AddHostedService<TelegramReviewWorker>();
}

var jobOptions = new TradingJobOptions(
    accountKey,
    Convert.ToHexString(
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                accountKey
                    + ":"
                    + tradingTelegram?.Token
                    + ":"
                    + tradingTelegram?.ChatId
                    + ":"
                    + tradingTelegram?.UserId
            )
        )
    ),
    tradingTelegram is not null
)
{
    UseHangfire = builder.Configuration.GetValue("Trading:UseHangfire", true),
};
builder.Services.AddSingleton(jobOptions);
var tradingReceipts = new SqlTradingNotificationReceiptStore(connectionString, accountKey, jobOptions.DestinationKey);
await tradingReceipts.Initialize(CancellationToken.None);
builder.Services.AddSingleton<ITradingNotificationReceiptStore>(tradingReceipts);
builder.Services.AddSingleton(
    new TradingNotificationDiagnostics(jobOptions.Enabled) { DashboardAvailable = jobOptions.UseHangfire }
);
if (jobOptions.UseHangfire)
{
    var dispatchStore = new SqlTradingJobDispatchStore(connectionString, accountKey);
    await dispatchStore.Initialize(CancellationToken.None);
    builder.Services.AddSingleton(dispatchStore);
    builder.Logging.AddFilter("Hangfire", LogLevel.Warning);
    builder.Services.AddHangfire(configuration =>
        configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(
                connectionString,
                new SqlServerStorageOptions
                {
                    SchemaName = "Hangfire",
                    SqlClientFactory = Microsoft.Data.SqlClient.SqlClientFactory.Instance,
                    QueuePollInterval = TimeSpan.FromSeconds(1),
                    CommandBatchMaxTimeout = TimeSpan.FromSeconds(30),
                    SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                    UseRecommendedIsolationLevel = true,
                    DisableGlobalLocks = true,
                    TryAutoDetectSchemaDependentOptions = false,
                }
            )
    );
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 1;
        options.Queues = ["telegram"];
        options.ServerName = "portfolio-telegram-" + Environment.MachineName;
        options.SchedulePollingInterval = TimeSpan.FromSeconds(1);
    });
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 1;
        options.Queues = ["maintenance"];
        options.ServerName = "portfolio-maintenance-" + Environment.MachineName;
    });
    builder.Services.AddTransient<TradingNotificationJob>();
    builder.Services.AddTransient<TradingReportJob>();
    builder.Services.AddHostedService<TradingNotificationScheduler>();
}

builder.Services.AddSingleton<IPortfolioRepository>(store);
builder.Services.AddSingleton<IReadOnlyPortfolioRepository>(store);
builder.Services.AddSingleton(credentials);
builder
    .Services.AddHttpClient<BitkubAccountReader>(http =>
    {
        http.BaseAddress = new Uri("https://api.bitkub.com");
        http.Timeout = TimeSpan.FromSeconds(20);
        http.MaxResponseContentBufferSize = bitkubMaximumResponseBytes;
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<IAccountEvidenceReader>(services => services.GetRequiredService<BitkubAccountReader>());
builder.Services.AddTransient<IManualSaleEvidenceReader, BitkubManualSaleEvidenceReader>();
builder.Services.AddTransient<IRiskEvidenceReader, BitkubRiskEvidenceReader>();
builder
    .Services.AddHttpClient<IOrderBroker, BitkubOrderBroker>(http =>
    {
        http.BaseAddress = new Uri("https://api.bitkub.com");
        http.Timeout = TimeSpan.FromSeconds(20);
        http.MaxResponseContentBufferSize = bitkubMaximumResponseBytes;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var marketDataUrl = new Uri(builder.Configuration["Services:MarketDataUrl"] ?? "http://127.0.0.1:5051");
if (marketDataUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(marketDataUrl.UserInfo))
{
    throw new InvalidOperationException("Configure a valid MarketData service URL without embedded credentials.");
}

builder
    .Services.AddHttpClient<ITradingHistoryReader, MarketDataTradingHistoryReader>(http =>
    {
        http.BaseAddress = marketDataUrl;
        http.Timeout = TimeSpan.FromSeconds(15);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder
    .Services.AddHttpClient(
        "market-events",
        http =>
        {
            http.BaseAddress = marketDataUrl;
            http.Timeout = Timeout.InfiniteTimeSpan;
        }
    )
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<ITradingEventSource>(services => new TradingEventSource(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("market-events"),
    builder.Configuration["MarketEvents:Token"] ?? ""
));

builder
    .Services.AddHttpClient<IMarketDataClient, MarketDataClient>(http =>
    {
        http.BaseAddress = marketDataUrl;
        http.Timeout = TimeSpan.FromSeconds(20);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder
    .Services.AddHttpClient<ITradingMarketReader, TradingMarketReader>(http =>
    {
        http.BaseAddress = marketDataUrl;
        http.Timeout = TimeSpan.FromSeconds(10);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder
    .Services.AddHttpClient<ITradingExchange, BitkubTradingExchange>(http =>
    {
        http.BaseAddress = new Uri("https://api.bitkub.com");
        http.Timeout = TimeSpan.FromSeconds(20);
        http.MaxResponseContentBufferSize = bitkubMaximumResponseBytes;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<PortfolioSync>();
builder.Services.AddSingleton<IPortfolioSync>(services => services.GetRequiredService<PortfolioSync>());
builder.Services.AddSingleton<IReadOnlyPortfolioSync>(services => services.GetRequiredService<PortfolioSync>());
builder.Services.AddHostedService(services => services.GetRequiredService<PortfolioSync>());
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        PortfolioJson.ConfigureOptions(options.JsonSerializerOptions);
    });
var app = builder.Build();
app.Use(
    async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        var tradingRequest =
            context.Request.Path.StartsWithSegments("/api/trading")
            || context.Request.Path.StartsWithSegments("/api/crypto/risk");
        if (tradingRequest)
        {
            if (
                !TradingRequestPolicy.Allows(
                    context.Request,
                    builder.Configuration["Hosting:InternalHost"],
                    tradingOptions
                )
            )
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Local authorized trading controls are required." }
                );
                return;
            }

            if (
                context.Request.ContentLength > 8192
                || !HttpMethods.IsGet(context.Request.Method)
                    && !await TradingRequestPolicy.BodyWithinLimit(context.Request, context.RequestAborted)
            )
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsJsonAsync(new { error = "Trading request is too large." });
                return;
            }
        }

        if (
            context.Request.Path.StartsWithSegments("/api/crypto/orders")
            || context.Request.Path.StartsWithSegments("/api/crypto/account")
        )
        {
            var origin = context.Request.Headers.Origin.ToString();
            var internalHost = builder.Configuration["Hosting:InternalHost"];
            var localHost =
                context.Request.Host.Host is "127.0.0.1" or "localhost" or "[::1]"
                || (
                    !string.IsNullOrWhiteSpace(internalHost)
                    && string.Equals(context.Request.Host.Host, internalHost, StringComparison.OrdinalIgnoreCase)
                );
            var allowedOrigin =
                string.IsNullOrEmpty(origin) || origin is "http://127.0.0.1:3000" or "http://localhost:3000";
            var mutation = !HttpMethods.IsGet(context.Request.Method);
            if (
                !localHost
                || !allowedOrigin
                || context.Request.Headers["Sec-Fetch-Site"] == "cross-site"
                || (
                    mutation
                    && (
                        context.Request.Headers["X-Quantara-Order-Action"] != "review"
                        || context.Request.ContentType?.StartsWith(
                            "application/json",
                            StringComparison.OrdinalIgnoreCase
                        ) != true
                    )
                )
            )
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Use the local order review screen to perform this action." }
                );
                return;
            }

            if (context.Request.ContentLength > 4000)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Order request is too large." });
                return;
            }
        }

        try
        {
            await next();
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (ArgumentException error) when (tradingRequest)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
        }
        catch (InvalidOperationException error) when (tradingRequest)
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
        }
        catch (BadHttpRequestException error) when (tradingRequest && error.StatusCode == 413)
        {
            context.Response.StatusCode = 413;
            await context.Response.WriteAsJsonAsync(new { error = "Trading request is too large." });
        }
        catch (OrderValidationException error)
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
        }
        catch (Exception)
        {
            await PortfolioFailureResponse.Write(context);
        }
    }
);
app.MapGet(
    "/health",
    async (CancellationToken cancellationToken) =>
    {
        await store.Load(cancellationToken);
        return Results.Ok(new { status = "ready" });
    }
);
if (jobOptions.UseHangfire)
{
    app.UseHangfireDashboard(
        "/api/trading/jobs",
        new DashboardOptions
        {
            Authorization = [new TradingDashboardAuthorizationFilter(tradingOptions)],
            IsReadOnlyFunc = _ => true,
            DisplayStorageConnectionString = false,
            AppPath = "/daily-brief",
        }
    );
    app.Services.GetRequiredService<IRecurringJobManager>()
        .AddOrUpdate<TradingReportJob>(
            "portfolio-report-" + accountKey,
            job => job.Prepare(accountKey, jobOptions.DestinationKey, CancellationToken.None),
            Cron.Minutely(),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }
        );
}

app.MapControllers();
await app.RunAsync();
