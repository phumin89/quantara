using System.Text;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingMarketTransportChecks
{
    private const string universeBody =
        "{\"observedAt\":\"2026-10-27T00:00:00Z\",\"symbols\":[],\"totalMarkets\":0,\"excludedMarkets\":0,\"error\":null}";

    private const string bookBody = "{\"markets\":[],\"errors\":[{\"symbol\":\"ETH\",\"error\":\"Unavailable\"}]}";

    public static async Task Run()
    {
        await Oversized(true, 1_048_576);
        await Oversized(false, 4_000_000);
        await MultibyteAllowance();
        await CharacterLimit();
        foreach (var universe in new[] { true, false })
        {
            await CallerCancellation(universe);
            await BodyDeadline(universe);
        }

        Console.WriteLine(
            "Trading market transport: bounded chunked bodies, multibyte character allowance, caller cancellation and configured body deadlines passed."
        );
    }

    private static async Task Oversized(bool universe, int limit)
    {
        var body = (universe ? universeBody : bookBody) + new string(' ', limit);
        var content = new TradingMarketTransportContent(body);
        using var handler = new TradingMarketTransportHandler(content, Path(universe));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://market.fixture.invalid/") };
        Check(content.Headers.ContentLength is null, "Unknown-length content must not have an admission header.");
        var rejected = false;
        try
        {
            await Read(new TradingMarketReader(http), universe, CancellationToken.None);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException)
        {
            rejected = true;
        }

        Check(rejected, "An oversized market response must be rejected.");
        Check(
            content.BytesProduced <= limit && content.BytesProduced < content.TotalBytes,
            "The transport byte budget must stop a chunked source before the whole payload is consumed."
        );
        Check(handler.Requests == 1, "Oversized evidence is not retried.");
    }

    private static async Task MultibyteAllowance()
    {
        const string prefix =
            "{\"markets\":[],\"errors\":[{\"symbol\":\"ETH\",\"error\":\"Unavailable\"}],\"padding\":\"";
        const string suffix = "\"}";
        var body = prefix + new string('ก', 999_900 - prefix.Length - suffix.Length) + suffix;
        var content = new TradingMarketTransportContent(body);
        using var handler = new TradingMarketTransportHandler(content, Path(false));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://market.fixture.invalid/") };
        var result = await new TradingMarketReader(http).ReadEvidence(["ETH"], CancellationToken.None);
        Check(
            result.Books.Count == 0
                && result.Errors.GetValueOrDefault("ETH") == "Unavailable"
                && content.BytesProduced == Encoding.UTF8.GetByteCount(body)
                && content.TotalBytes > 1_000_000,
            "Valid multibyte evidence near the existing character limit must remain readable."
        );
    }

    private static async Task CharacterLimit()
    {
        var content = new TradingMarketTransportContent(bookBody + new string(' ', 1_000_001));
        using var handler = new TradingMarketTransportHandler(content, Path(false));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://market.fixture.invalid/") };
        try
        {
            await new TradingMarketReader(http).ReadEvidence(["ETH"], CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            Check(content.BytesProduced == content.TotalBytes, "This case is below the separate byte limit.");
            return;
        }

        throw new InvalidOperationException("The existing character limit must remain in effect.");
    }

    private static async Task CallerCancellation(bool universe)
    {
        using var cancellation = new CancellationTokenSource();
        var body = (universe ? universeBody : bookBody) + new string(' ', 100_000);
        var content = new TradingMarketTransportContent(body, cancellation.Cancel);
        using var handler = new TradingMarketTransportHandler(content, Path(universe));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://market.fixture.invalid/") };
        try
        {
            await Read(new TradingMarketReader(http), universe, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Check(
                content.CancellationObserved && content.BytesProduced < content.TotalBytes,
                "Caller cancellation must stop body production instead of returning partial evidence."
            );
            return;
        }

        throw new InvalidOperationException("Caller cancellation must propagate from a market body read.");
    }

    private static async Task BodyDeadline(bool universe)
    {
        var content = new TradingMarketTransportContent(universe ? universeBody : bookBody, stall: true);
        using var handler = new TradingMarketTransportHandler(content, Path(universe));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://market.fixture.invalid/"),
            Timeout = TimeSpan.FromMilliseconds(200),
        };
        var pending = Read(new TradingMarketReader(http), universe, CancellationToken.None);
        var cancelled = false;
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            content.Release();
            try
            {
                await pending;
            }
            catch (OperationCanceledException) { }
        }

        Check(
            cancelled && content.CancellationObserved && content.BytesProduced > 0,
            "The configured timeout must cover a stalled body after headers and bytes have arrived."
        );
    }

    private static async Task Read(TradingMarketReader reader, bool universe, CancellationToken cancellationToken)
    {
        if (universe)
        {
            await reader.ReadUniverse(cancellationToken);
        }
        else
        {
            await reader.ReadEvidence(["ETH"], cancellationToken);
        }
    }

    private static string Path(bool universe) => universe ? "/api/market/trading/universe" : "/api/market/trading";

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
