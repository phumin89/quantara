using System.Globalization;
using System.Net;
using System.Text.Json;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubHistoryPacingTests
{
    [Fact]
    public async Task Next_history_page_waits_for_one_hundred_virtual_milliseconds_and_deduplicates_entries()
    {
        var clock = new ManualClock();
        var transport = new HistoryTransport(clock);
        using var broker = new BitkubLiveBroker("test-key", "test-secret", transport, clock);
        var history = broker.GetHistoryAsync("XRP-THB");
        Assert.Same(clock.TimerCreated.Task, await Task.WhenAny(clock.TimerCreated.Task, history));
        var timer = await clock.TimerCreated.Task;
        Assert.Equal(TimeSpan.FromMilliseconds(100), timer.InitialDelay);
        Assert.Single(transport.HistoryQueries);
        Assert.Equal(1, transport.ServerTimeRequests);
        Assert.False(history.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(99));
        Assert.Single(transport.HistoryQueries);
        Assert.Equal(1, transport.ServerTimeRequests);
        Assert.False(history.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var entries = await history;
        Assert.Equal(["txn-first", "txn-second"], entries.Select(e => e.TransactionId));
        Assert.All(entries, e => Assert.Equal("XRP-THB", e.Symbol));
        Assert.Equal(2, transport.HistoryQueries.Count);
        Assert.Equal("?sym=xrp_thb&pagination_type=keyset&lmt=100", transport.HistoryQueries[0]);
        Assert.Equal("?sym=xrp_thb&pagination_type=keyset&lmt=100&cursor=cursor%2B%2F%3D", transport.HistoryQueries[1]);
        Assert.Equal(2, transport.ServerTimeRequests);
        Assert.True(timer.Disposed);
    }

    [Fact]
    public async Task Cancellation_during_page_delay_stops_before_another_private_get_or_server_time_request()
    {
        var clock = new ManualClock();
        var transport = new HistoryTransport(clock);
        using var broker = new BitkubLiveBroker("test-key", "test-secret", transport, clock);
        using var cancellation = new CancellationTokenSource();
        var history = broker.GetHistoryAsync("XRP-THB", cancellation.Token);
        Assert.Same(clock.TimerCreated.Task, await Task.WhenAny(clock.TimerCreated.Task, history));
        var timer = await clock.TimerCreated.Task;
        Assert.Single(transport.HistoryQueries);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await history);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(timer.Disposed);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Single(transport.HistoryQueries);
        Assert.Equal(1, transport.ServerTimeRequests);
    }

    private static string Page(bool hasNext, params string[] transactions) => JsonSerializer.Serialize(new
    {
        error = 0,
        result = transactions.Select(id => new
        {
            txn_id = id, order_id = "order-" + id, parent_order_id = "parent", super_order_id = "super",
            client_id = "CLIENT123", side = "buy", type = "limit", rate = "100.00", amount = "1000.00",
            fee = "2.00", credit = "0.00", ts = 1699381086593L
        }),
        pagination = new { cursor = hasNext ? "cursor+/=" : null, has_next = hasNext }
    });

    private sealed class HistoryTransport(ManualClock clock) : HttpMessageHandler
    {
        public List<string> HistoryQueries { get; } = [];
        public int ServerTimeRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api.bitkub.com", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath == "/api/v3/servertime")
            {
                ServerTimeRequests++;
                Assert.False(request.Headers.Contains("X-BTK-APIKEY"));
                return Reply(clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            }
            Assert.Equal("/api/v3/market/my-order-history", request.RequestUri.AbsolutePath);
            Assert.True(request.Headers.Contains("X-BTK-SIGN"));
            HistoryQueries.Add(request.RequestUri.Query);
            return Reply(HistoryQueries.Count switch
            {
                1 => Page(true, "txn-first"),
                2 => Page(false, "txn-first", "txn-second"),
                _ => throw new InvalidOperationException("Unexpected extra history request")
            });
        }
        private static Task<HttpResponseMessage> Reply(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body) });
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(1699381086593);
        public TaskCompletionSource<ManualTimer> TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            var timer = new ManualTimer(callback, state, now + dueTime, dueTime);
            TimerCreated.SetResult(timer);
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            now += elapsed;
            if (TimerCreated.Task.IsCompletedSuccessfully) TimerCreated.Task.Result.FireIfDue(now);
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset due, TimeSpan initialDelay) : ITimer
    {
        public TimeSpan InitialDelay { get; } = initialDelay;
        public bool Disposed { get; private set; }
        public void FireIfDue(DateTimeOffset now) { if (!Disposed && now >= due) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
