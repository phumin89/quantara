using System.Security.Cryptography;
using System.Text;
using Mediator;
using Quantara.Api.Application;
using Quantara.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5050");
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var environmentFile = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".env"));
var settings = new Dictionary<string, string>();
if (File.Exists(environmentFile))
{
    foreach (var line in File.ReadLines(environmentFile))
    {
        var separator = line.IndexOf('=');
        if (separator <= 0) continue;
        var name = line[..separator].Trim();
        if (name is not ("BITKUB_API_KEY" or "BITKUB_API_SECRET")) continue;
        settings[name] = line[(separator + 1)..].Trim().Trim('"', '\'');
    }
}
string Credential(string name) => Environment.GetEnvironmentVariable(name) ?? settings.GetValueOrDefault(name, "");
var credentials = new BitkubCredentials(Credential("BITKUB_API_KEY"), Credential("BITKUB_API_SECRET"));
var accountKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.Key))).ToLowerInvariant();
var configuredConnection = builder.Configuration.GetConnectionString("Quantara");
var connectionString = configuredConnection ?? "Server=(localdb)\\MSSQLLocalDB;Database=Quantara;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;";
var store = new SqlPortfolioStore(connectionString, accountKey);
await store.Initialize(configuredConnection is null, CancellationToken.None);
builder.Services.AddSingleton<IPortfolioStore>(store);
builder.Services.AddSingleton(credentials);
builder.Services.AddHttpClient<BitkubReader>(http =>
{
    http.BaseAddress = new Uri("https://api.bitkub.com");
    http.Timeout = TimeSpan.FromSeconds(20);
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<PortfolioSync>();
builder.Services.AddSingleton<IPortfolioSync>(services => services.GetRequiredService<PortfolioSync>());
builder.Services.AddHostedService(services => services.GetRequiredService<PortfolioSync>());
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = "Portfolio storage is unavailable. Check the backend and SQL Server connection." });
    }
});
app.MapGet("/api/crypto/portfolio", async (IMediator mediator, CancellationToken cancellationToken) =>
    await mediator.Send(new GetPortfolio(), cancellationToken));
app.MapPost("/api/crypto/sync", async (IMediator mediator, CancellationToken cancellationToken) =>
{
    await mediator.Send(new RefreshPortfolio(), cancellationToken);
    return Results.Accepted();
});
app.MapGet("/api/crypto/history", async (string coin, IMediator mediator, CancellationToken cancellationToken) =>
{
    if (coin.Length is < 1 or > 30 || !coin.All(character => char.IsAsciiLetterOrDigit(character)))
        return Results.BadRequest(new { error = "Invalid coin symbol." });
    return Results.Ok(await mediator.Send(new GetCoinHistory(coin.ToUpperInvariant()), cancellationToken));
});
await app.RunAsync();
