using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application;
using Quantara.MarketData.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5051");
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Services.AddControllers();
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
