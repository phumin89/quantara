using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Infrastructure;

namespace TradingCompany.Api;

public sealed class CompanyConversationService(IConfiguration configuration, TimeProvider clock, ILogger<CompanyConversationService> logger, JsonForwardPaperStore paper) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private PostgresCompanyConversationStore? store;
    private volatile bool ready;

    public async Task<ConversationDates?> DatesAsync(CancellationToken cancellationToken) => ready && store is not null ? await store.DatesAsync(cancellationToken) : null;
    public async Task<CompanyConversation?> ReadAsync(DateOnly day, CancellationToken cancellationToken) => ready && store is not null ? await store.ReadAsync(day, cancellationToken) : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), clock);
        do
        {
            try
            {
                var file = configuration["Reports:ConnectionFile"] ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "report-db.json"));
                using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(file, stoppingToken));
                var connection = settings.RootElement.GetProperty("connectionString").GetString() ?? throw new InvalidDataException("Missing report database configuration.");
                store ??= new(connection);
                var root = configuration["TradingAgents:Company:ArtifactPath"] ?? Path.GetFullPath("data/agent-artifacts");
                var state = configuration["ForwardFunding:StatePath"] ?? Path.GetFullPath("data/forward-funding-v1.json");
                var journal = new JsonDailyMeetingJournal(state + ".paper-meetings.json");
                var artifacts = new List<AgentDecisionArtifact>();
                if (Directory.Exists(root))
                    foreach (var path in Directory.EnumerateFiles(root, "*.json"))
                    {
                        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out var id)) continue;
                        await using var stream = File.OpenRead(path);
                        var artifact = await JsonSerializer.DeserializeAsync<AgentDecisionArtifact>(stream, Json, stoppingToken) ?? throw new InvalidDataException("Invalid saved report.");
                        if (artifact.ArtifactId != id) throw new InvalidDataException("Report filename identity mismatch.");
                        artifacts.Add(artifact);
                    }
                var meetings = journal.ReadAll();
                var account = paper.Read();
                var executions = account is null ? [] : CompanyConversationProjection.PaperMessages(account);
                var dates = artifacts.Select(CompanyConversationProjection.BusinessDate).Concat(meetings.Select(m => m.BusinessDate)).Concat(executions.Select(e => e.Date))
                    .Append(ForwardFundingCalendar.BangkokDate(clock.GetUtcNow())).Distinct();
                var days = new List<CompanyConversation>();
                foreach (var date in dates)
                {
                    var meeting = meetings.SingleOrDefault(m => m.BusinessDate == date);
                    var messages = artifacts.Where(a => CompanyConversationProjection.BusinessDate(a) == date ||
                            meeting?.Summary?.Reports.Any(r => r.ArtifactId == a.ArtifactId.ToString()) == true)
                        .Select(a => CompanyConversationProjection.Project(a, meeting)).Concat(executions.Where(e => e.Date == date).Select(e => e.Message)).ToArray();
                    if (messages.Length > 0 || meeting is not null) days.Add(new(date, meeting?.Status, messages, clock.GetUtcNow(), account is not null));
                }
                await store.ImportAsync(days, stoppingToken);
                ready = true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                ready = false;
                // Never log connection strings, raw provider payloads or credentials.
                logger.LogWarning("Sandbox report database unavailable or import invalid; retrying automatically.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
