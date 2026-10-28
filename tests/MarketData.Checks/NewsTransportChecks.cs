using Quantara.MarketData.Infrastructure.News;

namespace Quantara.MarketData.Checks;

public static class NewsTransportChecks
{
    private static readonly Uri source = new("https://www.federalreserve.gov/feeds/press_all.xml");

    public static async Task Run()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var stalled = new StalledNewsContent();
        using var client = new HttpClient(new NewsHttpHandler(stalled)) { Timeout = TimeSpan.FromMilliseconds(250) };
        var timedOut = false;
        try
        {
            await new HttpNewsTransport(client).Fetch(source, null, null, deadline.Token);
        }
        catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
        {
            timedOut = true;
        }

        Check(timedOut && stalled.Started, "The request timeout also bounds a stalled response body.");

        using var cancelledBody = new StalledNewsContent();
        using var unlimited = new HttpClient(new NewsHttpHandler(cancelledBody)) { Timeout = Timeout.InfiniteTimeSpan };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var cancelled = false;
        try
        {
            await new HttpNewsTransport(unlimited).Fetch(source, null, null, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            cancelled = true;
        }

        Check(
            cancelled && cancelledBody.Started,
            "Caller cancellation releases a stalled body with no client deadline."
        );

        using var complete = new HttpClient(new FakeBitkubHandler("", "<rss/>"));
        var response = await new HttpNewsTransport(complete).Fetch(source, null, null, CancellationToken.None);
        Check(response.StatusCode == 200 && response.Body == "<rss/>", "A complete news response is preserved.");

        using var oversized = new HttpClient(new FakeBitkubHandler("", new string('x', 512_001)));
        var rejected = false;
        try
        {
            await new HttpNewsTransport(oversized).Fetch(source, null, null, CancellationToken.None);
        }
        catch (HttpRequestException)
        {
            rejected = true;
        }

        Check(rejected, "The response body byte budget remains enforced.");
        Console.WriteLine("News transport: body timeout, caller cancellation, successful read and byte budget passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
