using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TradingCompany.AgentRuntime;
using TradingCompany.Api;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using TradingCompany.Rehearsal;

namespace TradingCompany.Rehearsal;

internal static class RehearsalProgram
{
public static async Task<int> Main(string[] args)
{
if (!args.Contains("--live-codex", StringComparer.Ordinal))
{
    Console.WriteLine("Opt-in live rehearsal: --live-codex consumes signed-in Codex quota. No broker or bank access. Run from repository root.");
    return 2;
}
var repository = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(repository, "TradingCompany.sln"))) throw new InvalidOperationException("Run from Quantara repository root.");
var root = Path.Combine(repository, ".runtime", "rehearsals", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(root);
Console.WriteLine($"Isolated rehearsal: {root}");
var clock = TimeProvider.System;
using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var services = new ServiceCollection();
services.AddHttpClient("market-data", c => c.Timeout = TimeSpan.FromSeconds(20));
using var container = services.BuildServiceProvider();
var clients = container.GetRequiredService<IHttpClientFactory>();
var quotes = new BitkubForwardPaperQuotes(clients, clock);
var probes = new List<ForwardPaperQuote>();
foreach (var symbol in quotes.SupportedSymbols)
{
    var quote = await quotes.GetAsync(symbol, clock.GetUtcNow(), budget.Token);
    Console.WriteLine($"Quote {symbol}: {(quote is null ? "UNAVAILABLE" : "verified public depth")}");
    if (quote is not null) probes.Add(quote);
}
if (probes.Count == 0) throw new InvalidOperationException("No fresh quotes: no AI calls or orders were attempted.");
var funding = new JsonForwardFundingStore(Path.Combine(root, "funding.json"));
// Seed only this new test account's past salary dates; never modify the running app account or advance market time.
funding.Advance(clock.GetUtcNow().AddMonths(-4));
var treasury = funding.Advance(clock.GetUtcNow());
var legacy = new JsonForwardInvestmentStore(Path.Combine(root, "legacy.json"));
var paperPath = Path.Combine(root, "paper.json");
var journalPath = Path.Combine(root, "meetings.json");
var codex = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
var gateway = new ProgressGateway(new CodexCliTradingAgentGateway(codex));
var runner = new RecordingMeeting(new AiCompanyMeetingRunner(gateway,
    new JsonAgentDecisionArtifactStore(Path.Combine(root, "artifacts")),
    new(true, "codex-default", "medium", "company-v1", "isolated-paper-rehearsal-v1", 600, 8, 250000, TimeSpan.FromSeconds(120))));
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardFunding:Enabled"] = "true" }).Build();
ForwardPaperCompanyService CreateService() => new(funding, legacy, new(paperPath), runner,
    new RealHistoricalMarketDataProvider(clients), quotes, new JsonDailyMeetingJournal(journalPath), clock, configuration,
    NullLogger<ForwardPaperCompanyService>.Instance);
using var service = CreateService();
await service.TickAsync(budget.Token);
Console.WriteLine(JsonSerializer.Serialize(service.GetStatus()));
await Task.Delay(TimeSpan.FromSeconds(3), budget.Token); // A fill must use a snapshot observed after the AI decision.
await service.TickAsync(budget.Token);
var beforeRestart = new JsonForwardPaperStore(paperPath).Read()!;
var callsBeforeRestart = gateway.Calls;
using var restarted = CreateService();
await restarted.TickAsync(budget.Token);
var account = new JsonForwardPaperStore(paperPath).Read()!;
var final = ForwardPaperLedger.Snapshot(account);
var serviceStatus = JsonSerializer.SerializeToElement(restarted.GetStatus()).GetProperty("status").GetString()!;
var reconciled = RehearsalVerification.Passed(beforeRestart, account,
    ForwardPaperFunding.Pending(account, funding.Read()!) == 0, serviceStatus, callsBeforeRestart, gateway.Calls);
var report = new { mode = "isolated-live-data-paper-rehearsal", createdAt = clock.GetUtcNow(),
    salarySeed = "Four months of simulated salary in a NEW test account; not the user's saved treasury",
    liveOrders = false, reconciled, probes, company = restarted.GetStatus(),
    meeting = runner.Result, portfolio = final,
    limitations = new[] { "Paper-only. HOLD is a valid AI result; never force a trade to pass.",
        "Crypto fee 0.25% per side is a simulation assumption, not an account-specific fee quote.",
        "HTTP Date identifies snapshot response freshness, not the time of a market trade.",
        "No news, tax, or investment-performance proof. Deterministic buy/sell tests are separate from this AI result." } };
var reportPath = Path.Combine(root, "report.json");
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), budget.Token);
Console.WriteLine($"Report: {reportPath}");
Console.WriteLine($"Meeting={runner.Result?.Status ?? "NOT_RUN"}; calls={gateway.Calls}; receipts={final.Receipts.Count}; reconciled={reconciled}");
return reconciled && runner.Result?.Status == "COMPLETED" ? 0 : 1;
}
}

sealed class ProgressGateway(ITradingAgentGateway inner) : ITradingAgentGateway
{
    public int Calls { get; private set; }
    public async Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        Console.WriteLine($"AI {Calls}/8: {request.Role}");
        var result = await inner.CompleteStructuredAsync(request, cancellationToken);
        Console.WriteLine($"AI {request.Role}: returned structured response ({result.InputTokens + result.OutputTokens} tokens)");
        return result;
    }
}

sealed class RecordingMeeting(IAiCompanyMeetingRunner inner) : IAiCompanyMeetingRunner
{
    public bool Enabled => inner.Enabled;
    public string Model => inner.Model;
    public AiCompanyMeetingResult? Result { get; private set; }
    public async Task<AiCompanyMeetingResult> RunAsync(AiCompanyMeetingInput input, CancellationToken cancellationToken)
        => Result = await inner.RunAsync(input, cancellationToken);
}
