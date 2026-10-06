using System.Text.Json;
using TradingCompany.Api;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveCompanyActivityPublisherTests
{
    private static readonly LiveAccountId Account = new(Guid.Parse("ca692cd6-de98-4146-aa92-7cbb2a0df92a"));
    private static readonly TradingAccountContext Scope = new(TradingMode.Live, Account.Value.ToString("N"));
    private static readonly Guid Meeting = Guid.Parse("d79eb810-8e2b-4539-bfe7-bd82e00429fb");
    private static readonly Guid Other = Guid.Parse("e3a8e37f-9c58-4288-9467-e812d5b85c15");

    [Fact]
    public async Task Real_role_events_publish_only_activity_fields_and_heartbeat_preserves_change_time()
    {
        var client = new Client();
        var clock = new Clock();
        using var publisher = new LiveCompanyActivityPublisher(client, clock, Account);
        await publisher.PublishAsync(Event(clock.Now, "AgentStartedTask", "working"), CancellationToken.None);
        var started = Assert.Single(client.Activities);
        Assert.Equal(Scope, started.AccountContext);
        Assert.Equal(Meeting, started.MeetingId);
        Assert.Equal(new LiveRoleActivity("ceo", "working", clock.Now), Assert.Single(started.Roles));
        clock.Now = clock.Now.AddSeconds(15);
        await publisher.PublishHeartbeatAsync(CancellationToken.None);
        Assert.Equal(clock.Now, client.Activities[^1].ObservedAt);
        Assert.Equal(started.Roles[0].ChangedAt, client.Activities[^1].Roles[0].ChangedAt);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(started, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(["accountContext", "meetingId", "observedAt", "roles", "researchSymbols", "tradingSymbols"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["role", "status", "changedAt"], json.RootElement.GetProperty("roles")[0].EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("private model text", json.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("foreign-account")]
    [InlineData("sandbox")]
    [InlineData("missing-scope")]
    [InlineData("empty-meeting")]
    [InlineData("payload-meeting")]
    [InlineData("role")]
    [InlineData("type")]
    [InlineData("status")]
    [InlineData("stale")]
    [InlineData("future")]
    public async Task Invalid_or_foreign_events_never_create_workers(string defect)
    {
        var client = new Client();
        var clock = new Clock();
        using var publisher = new LiveCompanyActivityPublisher(client, clock, Account);
        var message = Event(clock.Now, "AgentStartedTask", "working");
        var payload = (AgentRuntimeEventPayload)message.Payload;
        message = defect switch
        {
            "foreign-account" => message with { AccountContext = new(TradingMode.Live, Other.ToString("N")) },
            "sandbox" => message with { AccountContext = new(TradingMode.Sandbox, Scope.AccountId) },
            "missing-scope" => message with { AccountContext = null },
            "empty-meeting" => message with { CorrelationId = Guid.Empty, Payload = payload with { MeetingId = Guid.Empty } },
            "payload-meeting" => message with { Payload = payload with { MeetingId = Other } },
            "role" => message with { Payload = payload with { Role = "invented-worker" } },
            "type" => message with { Type = "OrderSubmitted" },
            "status" => message with { Payload = payload with { Status = "idle" } },
            "stale" => message with { OccurredAt = clock.Now.AddSeconds(-46) },
            _ => message with { OccurredAt = clock.Now.AddSeconds(1) }
        };
        await publisher.PublishAsync(message, CancellationToken.None);
        Assert.Empty(client.Activities);
        await publisher.PublishHeartbeatAsync(CancellationToken.None);
        var empty = Assert.Single(client.Activities);
        Assert.Null(empty.MeetingId);
        Assert.Empty(empty.Roles);
    }

    [Fact]
    public async Task Ordered_events_allow_only_one_working_role_and_new_meeting_resets_roster()
    {
        var client = new Client();
        var clock = new Clock();
        using var publisher = new LiveCompanyActivityPublisher(client, clock, Account);
        await publisher.PublishAsync(Event(clock.Now, "AgentStartedTask", "working"), CancellationToken.None);
        await publisher.PublishAsync(Event(clock.Now, "AgentStartedTask", "working", "chief-of-staff"), CancellationToken.None);
        Assert.Single(client.Activities);
        await publisher.PublishAsync(Event(clock.Now.AddSeconds(-1), "AgentCompletedTask", "completed"), CancellationToken.None);
        Assert.Single(client.Activities);
        await publisher.PublishAsync(Event(clock.Now, "AgentCompletedTask", "completed"), CancellationToken.None);
        await publisher.PublishAsync(Event(clock.Now, "AgentStartedTask", "working", "chief-of-staff"), CancellationToken.None);
        Assert.Equal(2, client.Activities[^1].Roles.Count);
        Assert.Single(client.Activities[^1].Roles, role => role.Status == "working");
        clock.Now = clock.Now.AddSeconds(1);
        var next = Event(clock.Now, "AgentSpawned", "idle", "market-research", Other);
        await publisher.PublishAsync(next, CancellationToken.None);
        Assert.Equal(next.CorrelationId, client.Activities[^1].MeetingId);
        Assert.Equal("market-research", Assert.Single(client.Activities[^1].Roles).Role);
    }

    [Fact]
    public async Task End_meeting_clears_unfinished_work_without_inventing_a_successful_result()
    {
        var client = new Client();
        var clock = new Clock();
        using var publisher = new LiveCompanyActivityPublisher(client, clock, Account);
        await publisher.PublishAsync(Event(clock.Now, "AgentStartedTask", "working"), CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(2);
        await publisher.EndMeetingAsync(CancellationToken.None);
        var ended = client.Activities[^1];
        Assert.Equal(new LiveRoleActivity("ceo", "idle", clock.Now), Assert.Single(ended.Roles));
        await publisher.PublishAsync(Event(clock.Now.AddSeconds(-1), "AgentCompletedTask", "completed"), CancellationToken.None);
        Assert.Equal(2, client.Activities.Count);
    }

    [Fact]
    public async Task Failed_report_remains_failed_and_telemetry_errors_do_not_interrupt_work()
    {
        var client = new Client { Throw = true };
        var clock = new Clock();
        using var publisher = new LiveCompanyActivityPublisher(client, clock, Account);
        await publisher.PublishAsync(Event(clock.Now, "AgentStartedTask", "working"), CancellationToken.None);
        await publisher.PublishHeartbeatAsync(CancellationToken.None);
        await publisher.EndMeetingAsync(CancellationToken.None);
        client.Throw = false;
        await publisher.PublishAsync(Event(clock.Now, "AgentFailed", "failed"), CancellationToken.None);
        await publisher.EndMeetingAsync(CancellationToken.None);
        Assert.Equal("failed", Assert.Single(Assert.Single(client.Activities).Roles).Status);
    }

    [Fact]
    public async Task Requested_cancellation_is_propagated_without_a_heartbeat()
    {
        var client = new Client();
        using var publisher = new LiveCompanyActivityPublisher(client, new Clock(), Account);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishHeartbeatAsync(cancellation.Token));
        Assert.Empty(client.Activities);
    }

    [Fact]
    public async Task Cancellation_during_transport_is_not_swallowed_as_a_telemetry_outage()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new Client { BeforeSave = cancellation.Cancel };
        var clock = new Clock();
        using var publisher = new LiveCompanyActivityPublisher(client, clock, Account);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(
            Event(clock.Now, "AgentStartedTask", "working"), cancellation.Token));
        Assert.Empty(client.Activities);
    }

    private static RealtimeEvent Event(DateTimeOffset at, string type, string status, string role = "ceo", Guid? meeting = null)
        => new(type, meeting ?? Meeting, at, new AgentRuntimeEventPayload(role, role, status,
            "private model text", "ResearchDesk", "private model text", MeetingId: meeting ?? Meeting,
            Result: new { prompt = "private model text" })) { AccountContext = Scope };

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 6, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Client : ILiveCompanyClient
    {
        public List<LiveCompanyActivity> Activities { get; } = [];
        public bool Throw { get; set; }
        public Action? BeforeSave { get; init; }
        public Task SaveActivityAsync(LiveCompanyActivity activity, CancellationToken cancellationToken)
        {
            BeforeSave?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw) throw new HttpRequestException("fixture telemetry outage");
            Activities.Add(activity);
            return Task.CompletedTask;
        }
        public Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveMeetingAsync(DailyMeetingAttempt attempt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
