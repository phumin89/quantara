using System.Text.Json;
using System.Text.Json.Serialization;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;
using Quantara.MarketData.Infrastructure;
using Quantara.MarketData.Infrastructure.News;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Hosting:ListenUrl"] ?? "http://127.0.0.1:5051");
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter<SignalDirection>(JsonNamingPolicy.CamelCase, false)
        )
    );
builder.Services.AddSingleton(TimeProvider.System);
var marketConnection = builder.Configuration.GetConnectionString("MarketData");
var newsStore = new SqlNewsStore(
    marketConnection
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=QuantaraMarketData;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;",
    marketConnection is null
);
builder.Services.AddSingleton(newsStore);
builder.Services.AddSingleton<Quantara.MarketData.Application.News.INewsReader>(newsStore);
builder
    .Services.AddHttpClient<INewsTransport, HttpNewsTransport>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/xml, text/xml");
    })
    .ConfigurePrimaryHttpMessageHandler(HttpNewsTransport.CreateHandler);
builder.Services.AddHostedService<NewsPoller>();
builder.Services.AddSingleton<MarketEventBus>();
builder.Services.AddSingleton<IMarketEventBus>(services => services.GetRequiredService<MarketEventBus>());
builder.Services.AddSingleton<IMarketEventReader>(services => services.GetRequiredService<MarketEventBus>());
builder.Services.AddSingleton<IIntradayCandleStore>(
    new SqlIntradayCandleStore(
        marketConnection
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=QuantaraMarketData;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;",
        marketConnection is null
    )
);
builder
    .Services.AddHttpClient(
        "live-market",
        http =>
        {
            http.BaseAddress = new Uri("https://api.bitkub.com");
            http.Timeout = TimeSpan.FromSeconds(10);
        }
    )
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(services => new LiveMarketFeed(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("live-market"),
    services.GetRequiredService<IIntradayCandleStore>(),
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<MarketEventBus>()
));
builder.Services.AddSingleton<IIntradayMarketReader>(services => services.GetRequiredService<LiveMarketFeed>());
builder.Services.AddHostedService(services => services.GetRequiredService<LiveMarketFeed>());
builder.Services.AddSingleton<IExecutionMarketReader>(services => new BitkubExecutionMarketReader(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("live-market"),
    services.GetRequiredService<IIntradayMarketReader>(),
    services.GetRequiredService<TimeProvider>()
));
builder
    .Services.AddHttpClient<BitkubMarketDataReader>(client =>
    {
        client.BaseAddress = new Uri("https://api.bitkub.com");
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IMarketDataReader>(services => services.GetRequiredService<BitkubMarketDataReader>());
builder
    .Services.AddHttpClient<IResearchMarketReader, BitkubResearchMarketReader>(client =>
    {
        client.BaseAddress = new Uri("https://api.bitkub.com");
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

var app = builder.Build();
app.Use(
    async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            await next();
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (HttpRequestException)
        {
            await WriteUnavailable(context);
        }
        catch (JsonException)
        {
            await WriteUnavailable(context);
        }
        catch (InvalidDataException)
        {
            await WriteUnavailable(context);
        }
        catch (KeyNotFoundException)
        {
            await WriteUnavailable(context);
        }
        catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await WriteUnavailable(context);
        }
    }
);
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapControllers();
await app.RunAsync();

static Task WriteUnavailable(HttpContext context)
{
    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    return context.Response.WriteAsJsonAsync(
        new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Market data is unavailable.",
            Detail = "The public market data provider could not be reached or returned an invalid response.",
        }
    );
}
