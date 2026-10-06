using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using TradingCompany.AgentRuntime;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;

namespace TradingCompany.Api;

// The Live advisory entry point registers no Sandbox scheduler, broker or ledger access.
// Run it in the separate Compose company service, which has no broker/TLS private-key mounts.
public static class LiveCompanyHost
{
    public static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var configFile = Environment.GetEnvironmentVariable("QUANTARA_LIVE_COMPANY_CONFIG");
        if (!string.IsNullOrWhiteSpace(configFile))
        {
            if (!Path.IsPathFullyQualified(configFile)) throw new InvalidDataException("Company config path must be absolute.");
            builder.Configuration.AddJsonFile(configFile, optional: false, reloadOnChange: false);
        }
        if (builder.Configuration.GetValue<bool>("LiveCompany:Enabled")) Configure(builder.Services, builder.Configuration);
        using var host = builder.Build();
        await host.RunAsync();
    }

    public static void Configure(IServiceCollection services, IConfiguration config)
    {
        if (!config.GetValue<bool>("LiveCompany:Enabled")) return;
        if (!Guid.TryParse(config["LiveCompany:AccountId"], out var id) || id == Guid.Empty ||
            !string.IsNullOrWhiteSpace(config["Bitkub:CredentialsFile"]) ||
            !string.IsNullOrWhiteSpace(config.GetConnectionString("LiveTrading")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BITKUB_API_KEY")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BITKUB_API_SECRET")))
            throw new InvalidDataException("Isolated Live company configuration is invalid.");
        var account = new LiveAccountId(id);
        var token = config["LiveCompany:CoordinatorToken"];
        var dataPath = config["LiveCompany:DataPath"];
        var model = config["LiveCompany:Model"];
        if (token is null || token.Length is < 32 or > 256 || token.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(dataPath ?? "") || string.IsNullOrWhiteSpace(model))
            throw new InvalidDataException("Isolated Live company configuration is invalid.");
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILiveCompanyClient>(sp => new LiveExecutionClient(
            CreateTransport(config["LiveCompany:ExecutorCertificateFile"]), token, account, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new LiveCompanyActivityPublisher(sp.GetRequiredService<ILiveCompanyClient>(),
            sp.GetRequiredService<TimeProvider>(), account));
        services.AddSingleton<IRealtimeEventPublisher>(sp => sp.GetRequiredService<LiveCompanyActivityPublisher>());
        services.AddHostedService(sp => sp.GetRequiredService<LiveCompanyActivityPublisher>());
        services.AddHttpClient("market-data", client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<IHistoricalMarketEvidenceProvider, RealHistoricalMarketDataProvider>();
        services.AddSingleton<ITradingAgentGateway>(_ => new CodexCliTradingAgentGateway("codex"));
        services.AddSingleton<IAgentDecisionArtifactStore>(_ => new JsonAgentDecisionArtifactStore(Path.Combine(dataPath!, "artifacts")));
        services.AddSingleton<IAiCompanyMeetingRunner>(sp => new AiCompanyMeetingRunner(
            sp.GetRequiredService<ITradingAgentGateway>(), sp.GetRequiredService<IAgentDecisionArtifactStore>(),
            new(true, model, "medium", "company-v1", "owner-live-v1", 600, 8, 250_000, TimeSpan.FromSeconds(120)),
            clock: sp.GetRequiredService<TimeProvider>().GetUtcNow,
            events: sp.GetRequiredService<IRealtimeEventPublisher>()));
        services.AddSingleton<IDailyMeetingJournal>(_ => new JsonDailyMeetingJournal(Path.Combine(dataPath!, "meetings.json"),
            new(TradingMode.Live, id.ToString("N"))));
        services.AddSingleton(sp => new LiveCompanyCoordinator(sp.GetRequiredService<ILiveCompanyClient>(),
            sp.GetRequiredService<IHistoricalMarketEvidenceProvider>(), sp.GetRequiredService<IAiCompanyMeetingRunner>(),
            sp.GetRequiredService<IDailyMeetingJournal>(), sp.GetRequiredService<TimeProvider>(), account, continuousReviews: true));
        services.AddHostedService<LiveCompanyWorker>();
    }

    public static HttpClient CreateTransport(string? publicCertificateFile)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
            return LiveExecutionClient.CreateTransport();
        if (!Path.IsPathFullyQualified(publicCertificateFile ?? "") || new FileInfo(publicCertificateFile!).Length > 64 * 1024)
            throw new InvalidDataException("Executor trust certificate is required.");
        var certificate = X509CertificateLoader.LoadCertificateFromFile(publicCertificateFile!);
        if (certificate.HasPrivateKey || certificate.Subject != "CN=localhost") throw new InvalidDataException("Public localhost certificate is required.");
        var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
        policy.CustomTrustStore.Add(certificate);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy },
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (context.DnsEndPoint.Host != "localhost" || context.DnsEndPoint.Port != 7443)
                    throw new InvalidDataException("Unexpected executor endpoint.");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync("owner", 7443, cancellationToken); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            }
        };
        // The request URI stays localhost. Normal certificate name/chain validation is preserved.
        return new HttpClient(handler);
    }
}

public sealed class LiveCompanyWorker(LiveCompanyCoordinator company, TimeProvider clock, LiveCompanyActivityPublisher activity) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        try
        {
            do
            {
                try { await company.TickAsync(stoppingToken); }
                finally
                {
                    activity.UpdateCoordinatorStatus(company.Status, company.ResearchUniverse, company.TradingUniverse);
                    await activity.EndMeetingAsync(stoppingToken);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
