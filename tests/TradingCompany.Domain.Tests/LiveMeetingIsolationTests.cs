using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using TradingCompany.Api;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveMeetingIsolationTests
{
    [Fact]
    public void Live_claim_survives_restart_and_cannot_be_read_as_paper_or_another_account()
    {
        var root = Path.Combine(Path.GetTempPath(), "quantara-scope-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "meetings.json");
        var context = new TradingAccountContext(TradingMode.Live, "11111111111111111111111111111111");
        var day = new DateOnly(2026, 9, 30);
        var attempt = Guid.NewGuid();
        try
        {
            var live = new JsonDailyMeetingJournal(path, context);
            Assert.True(live.TryBegin(day, attempt));
            live.Complete(day, attempt, "HOLD");
            var restored = new JsonDailyMeetingJournal(path, context);
            Assert.Equal(context, restored.Read(day)!.AccountContext);
            Assert.False(restored.TryBegin(day, Guid.NewGuid()));
            Assert.Throws<InvalidDataException>(() => new JsonDailyMeetingJournal(path).Read(day));
            Assert.Throws<InvalidDataException>(() => new JsonDailyMeetingJournal(path, context with
                { AccountId = "22222222222222222222222222222222" }).Read(day));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Public_paper_publisher_refuses_live_events_before_accessing_public_clients()
    {
        var message = new RealtimeEvent("AgentCompletedTask", Guid.NewGuid(), DateTimeOffset.UtcNow, new { summary = "private" })
        { AccountContext = new(TradingMode.Live, "11111111111111111111111111111111") };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SignalRRealtimeEventPublisher(null!).PublishAsync(message, default));
    }
}
