using System.Security.Cryptography;
using System.Text;
using Mediator;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5050");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
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
builder.Services.AddSingleton<IOrderStore>(orders);
builder.Services.AddSingleton<IReadOnlyOrderStore>(orders);
builder.Services.AddHostedService<OrderStatusMonitor>();
builder.Services.AddSingleton(TimeProvider.System);
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
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddSingleton<TelegramOrderReviews>();
    builder.Services.AddHostedService<TelegramReviewWorker>();
}

builder.Services.AddSingleton<IPortfolioRepository>(store);
builder.Services.AddSingleton<IReadOnlyPortfolioRepository>(store);
builder.Services.AddSingleton(credentials);
builder
    .Services.AddHttpClient<BitkubAccountReader>(http =>
    {
        http.BaseAddress = new Uri("https://api.bitkub.com");
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<IAccountEvidenceReader>(services => services.GetRequiredService<BitkubAccountReader>());
builder
    .Services.AddHttpClient<IOrderBroker, BitkubOrderBroker>(http =>
    {
        http.BaseAddress = new Uri("https://api.bitkub.com");
        http.Timeout = TimeSpan.FromSeconds(20);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var marketDataUrl = new Uri(builder.Configuration["Services:MarketDataUrl"] ?? "http://127.0.0.1:5051");
if (marketDataUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(marketDataUrl.UserInfo))
{
    throw new InvalidOperationException("Configure a valid MarketData service URL without embedded credentials.");
}

builder
    .Services.AddHttpClient<IMarketDataClient, MarketDataClient>(http =>
    {
        http.BaseAddress = marketDataUrl;
        http.Timeout = TimeSpan.FromSeconds(20);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<PortfolioSync>();
builder.Services.AddSingleton<IPortfolioSync>(services => services.GetRequiredService<PortfolioSync>());
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
        if (
            context.Request.Path.StartsWithSegments("/api/crypto/orders")
            || context.Request.Path.StartsWithSegments("/api/crypto/account")
        )
        {
            var origin = context.Request.Headers.Origin.ToString();
            var localHost = context.Request.Host.Host is "127.0.0.1" or "localhost" or "[::1]";
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
        catch (OrderValidationException error)
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
        }
        catch (Exception)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(
                new { error = "Portfolio storage is unavailable. Check the service and SQL Server connection." }
            );
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
app.MapControllers();
await app.RunAsync();
