using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.Api;

// Display telemetry only. Meeting output and financial authority never enter this projection.
public sealed class LiveCompanyActivityPublisher : BackgroundService, IRealtimeEventPublisher
{
    private readonly ILiveCompanyClient client;
    private readonly TimeProvider clock;
    private readonly TradingAccountContext scope;
    private readonly object stateLock = new();
    private readonly SemaphoreSlim sends = new(1, 1);
    private readonly Dictionary<string, LiveRoleActivity> roles = new(StringComparer.Ordinal);
    private Guid? meetingId;
    private DateTimeOffset lastEventAt = DateTimeOffset.MinValue;
    private string? coordinatorStatus;
    private IReadOnlyList<string> researchSymbols = [];
    private IReadOnlyList<string> tradingSymbols = [];
    private IReadOnlyList<LiveMeetingIssue> issues = [];
    private DateTimeOffset? nextRetryAt;

    public LiveCompanyActivityPublisher(ILiveCompanyClient client, TimeProvider clock, LiveAccountId account)
    {
        if (account.Value == Guid.Empty) throw new ArgumentException("Live account is required.", nameof(account));
        this.client = client;
        this.clock = clock;
        scope = new(TradingMode.Live, account.Value.ToString("N"));
    }

    public async Task PublishAsync(RealtimeEvent message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = message.Type switch
        {
            "AgentSpawned" => "idle",
            "AgentStartedTask" => "working",
            "AgentCompletedTask" => "completed",
            "AgentFailed" => "failed",
            _ => null
        };
        if (message.AccountContext != scope || message.CorrelationId == Guid.Empty || status is null ||
            message.Payload is not AgentRuntimeEventPayload payload || payload.MeetingId != message.CorrelationId ||
            !LiveActivityValidation.IsRole(payload.Role) || payload.Status != status) return;

        await sends.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            if (now - message.OccurredAt >= LiveActivityValidation.Lifetime || message.OccurredAt > now) return;
            lock (stateLock)
            {
                if (message.OccurredAt < lastEventAt) return;
                var newMeeting = meetingId != message.CorrelationId;
                if (!newMeeting && status == "working" && roles.Values.Any(role => role.Role != payload.Role && role.Status == "working"))
                    return;
                if (newMeeting) roles.Clear();
                meetingId = message.CorrelationId;
                roles[payload.Role] = new(payload.Role, status, message.OccurredAt);
                lastEventAt = message.OccurredAt;
            }
            await SendSnapshotAsync(cancellationToken);
        }
        finally { sends.Release(); }
    }

    public async Task EndMeetingAsync(CancellationToken cancellationToken)
    {
        await sends.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            lock (stateLock)
            {
                var working = roles.Values.Where(role => role.Status == "working").ToArray();
                if (working.Length == 0) return;
                foreach (var role in working) roles[role.Role] = role with { Status = "idle", ChangedAt = now };
                lastEventAt = now > lastEventAt ? now : lastEventAt;
            }
            await SendSnapshotAsync(cancellationToken);
        }
        finally { sends.Release(); }
    }

    public async Task PublishHeartbeatAsync(CancellationToken cancellationToken)
    {
        await sends.WaitAsync(cancellationToken);
        try { await SendSnapshotAsync(cancellationToken); }
        finally { sends.Release(); }
    }

    public void UpdateCoordinatorStatus(string status, IReadOnlyList<string> research, IReadOnlyList<string> trading,
        IReadOnlyList<LiveMeetingIssue>? problems = null, DateTimeOffset? retryAt = null)
    {
        lock (stateLock)
        {
            coordinatorStatus = status;
            researchSymbols = research.ToArray();
            tradingSymbols = trading.ToArray();
            issues = problems?.ToArray() ?? [];
            nextRetryAt = retryAt;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), clock);
        try
        {
            do { await PublishHeartbeatAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task SendSnapshotAsync(CancellationToken cancellationToken)
    {
        LiveCompanyActivity snapshot;
        lock (stateLock) snapshot = new(scope, meetingId, clock.GetUtcNow(), roles.Values.ToArray())
        { CoordinatorStatus = coordinatorStatus, ResearchSymbols = researchSymbols, TradingSymbols = tradingSymbols,
            Issues = issues.Count == 0 ? null : issues, NextRetryAt = nextRetryAt };
        try { await client.SaveActivityAsync(snapshot, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { } // Telemetry transport cannot interrupt meeting or order validation.
    }
}
