using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingEventSourceChecks
{
    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        var body =
            $"data: {{\"kind\":\"quoteChanged\",\"symbol\":\"ETH\",\"sequence\":1,\"observedAt\":\"{now:O}\",\"watchedSymbols\":[\"BTC\",\"ETH\"]}}\n";
        var handler = new TradingEventSourceFakeHandler(body);
        var source = new TradingEventSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            "secret"
        );
        var events = new List<TradingMarketEvent>();
        await foreach (var item in source.Read(["BTC"], CancellationToken.None))
        {
            events.Add(item);
        }

        if (events.Count != 1 || events[0].Symbol != "ETH" || handler.Headers.Count != 2)
        {
            throw new InvalidOperationException("SSE source contract failed.");
        }

        if (handler.Headers.Any(x => x != "secret"))
        {
            throw new InvalidOperationException("SSE auth header missing.");
        }

        if (handler.WatchBody is null || !handler.WatchBody.Contains("BTC", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Required watch symbol was not posted.");
        }

        var duplicateBody =
            $"data: {{\"kind\":\"quoteChanged\",\"symbol\":\"ETH\",\"sequence\":2,\"observedAt\":\"{now:O}\"}}\ndata: {{\"kind\":\"quoteChanged\",\"symbol\":\"ETH\",\"sequence\":1,\"observedAt\":\"{now:O}\"}}\n";
        var reconnectBody =
            $"data: {{\"kind\":\"quoteChanged\",\"symbol\":\"ETH\",\"sequence\":1,\"observedAt\":\"{now:O}\"}}\n";
        var reconnectHandler = new TradingEventSourceFakeHandler(duplicateBody, reconnectBody);
        var reconnectSource = new TradingEventSource(
            new HttpClient(reconnectHandler) { BaseAddress = new Uri("http://localhost/") },
            "secret"
        );
        var firstRead = await ReadAll(reconnectSource, ["BTC"]);
        var secondRead = await ReadAll(reconnectSource, ["BTC"]);
        if (firstRead.Count != 1 || firstRead[0].Sequence != 2 || secondRead.Count != 1 || secondRead[0].Sequence != 1)
        {
            throw new InvalidOperationException("Dedupe must reset for reconnects.");
        }

        await ExpectInvalid(
            $"data: {{\"kind\":\"quoteChanged\",\"symbol\":\"ETH\",\"sequence\":1,\"observedAt\":\"{now.AddMinutes(-1):O}\"}}\n"
        );
        await ExpectInvalid(
            $"data: {{\"kind\":\"quoteChanged\",\"symbol\":\"ETH\",\"sequence\":1,\"observedAt\":\"{now.AddMinutes(1):O}\"}}\n"
        );
        await ExpectInvalid(
            "data: {\"kind\":\"bookChanged\",\"symbol\":\"ETH\",\"sequence\":1,\"observedAt\":\"bad\",\"book\":{}}\n"
        );
        await ExpectInvalid("data: " + new string('x', 131073) + "\n");

        var validBook =
            $"data: {{\"kind\":\"bookChanged\",\"symbol\":\"ETH\",\"sequence\":2,\"observedAt\":\"{now:O}\",\"book\":{{\"symbol\":\"ETH\",\"observedAt\":\"{now:O}\",\"bid\":1,\"ask\":2,\"bidQuantity\":3,\"askQuantity\":4,\"bars\":[]}}}}\n";
        var validHandler = new TradingEventSourceFakeHandler(validBook);
        var validSource = new TradingEventSource(
            new HttpClient(validHandler) { BaseAddress = new Uri("http://localhost/") },
            "secret"
        );
        if ((await ReadAll(validSource, ["BTC"])).Count != 1)
        {
            throw new InvalidOperationException("Valid book event was rejected.");
        }

        await ExpectInvalid(validBook.Replace("\"bid\":1", "\"bid\":3", StringComparison.Ordinal));
        await ExpectInvalid(validBook.Replace("bookChanged", "research", StringComparison.Ordinal));
        await ExpectInvalid(
            validBook.Replace(
                $"\"book\":{{\"symbol\":\"ETH\",\"observedAt\":\"{now:O}\"",
                $"\"book\":{{\"symbol\":\"ETH\",\"observedAt\":\"{now.AddMinutes(1):O}\"",
                StringComparison.Ordinal
            )
        );

        await ExpectInvalid(
            $"data: {{\"kind\":\"bookChanged\",\"symbol\":\"ETH\",\"sequence\":2,\"observedAt\":\"{now:O}\",\"book\":{{\"symbol\":\"BTC\",\"observedAt\":\"{now:O}\",\"bid\":1,\"ask\":2,\"bidQuantity\":3,\"askQuantity\":4,\"bars\":[]}}}}\n"
        );

        var heartbeat =
            $"data: {{\"kind\":\"heartbeat\",\"symbol\":\"\",\"sequence\":0,\"observedAt\":\"{now:O}\"}}\ndata: {{\"kind\":\"heartbeat\",\"symbol\":\"\",\"sequence\":0,\"observedAt\":\"{now:O}\"}}\ndata: {{\"kind\":\"heartbeat\",\"symbol\":\"\",\"sequence\":0,\"observedAt\":\"{now:O}\"}}\n";
        var heartbeatHandler = new TradingEventSourceFakeHandler(heartbeat);
        var heartbeatSource = new TradingEventSource(
            new HttpClient(heartbeatHandler) { BaseAddress = new Uri("http://localhost/") },
            "secret"
        );
        if ((await ReadAll(heartbeatSource, ["BTC"])).Count != 3)
        {
            throw new InvalidOperationException("Heartbeats must not be deduplicated.");
        }

        var stalledSource = new TradingEventSource(
            new HttpClient(new TradingEventSourceStallingHandler()) { BaseAddress = new Uri("http://localhost/") },
            "secret",
            TimeSpan.FromMilliseconds(20)
        );
        try
        {
            await ReadAll(stalledSource, ["BTC"]);
            throw new InvalidOperationException("Stalled stream was not cancelled.");
        }
        catch (OperationCanceledException) { }

        Console.WriteLine(
            "Private SSE: exact watch/auth, validated books, stale/future rejection, bounded frames, dedupe/reconnect and heartbeat timeout passed."
        );
    }

    private static async Task<List<TradingMarketEvent>> ReadAll(
        TradingEventSource source,
        IReadOnlyList<string> required
    )
    {
        var result = new List<TradingMarketEvent>();
        await foreach (var item in source.Read(required, CancellationToken.None))
        {
            result.Add(item);
        }

        return result;
    }

    private static async Task ExpectInvalid(string body)
    {
        var handler = new TradingEventSourceFakeHandler(body);
        var source = new TradingEventSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            "secret"
        );
        try
        {
            await ReadAll(source, ["BTC"]);
        }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
        {
            return;
        }

        throw new InvalidOperationException("Malformed event was accepted.");
    }
}
