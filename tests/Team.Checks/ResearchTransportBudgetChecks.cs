using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchTransportBudgetChecks
{
    public static async Task Run()
    {
        await SnapshotCollectionChecks.Run();
        var baseContent = new ChunkedResearchContent(new string('x', 200_001));
        using var baseResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = baseContent };
        using var baseHandler = new SignalHttpHandler(_ => baseResponse);
        using var baseHttp = new HttpClient(baseHandler) { BaseAddress = new Uri("http://fixture.invalid") };
        await ExpectFailure(
            () => new MarketResearchReader(baseHttp).Read(new string('a', 64), CancellationToken.None),
            "base market research rejects before consuming the whole unknown-length body"
        );
        Check(baseContent.BytesProduced < 200_001, "base response budget stops chunk production early");

        var validBase = JsonSerializer.Serialize(new { scan = new { symbols = Array.Empty<string>() } });
        var nativeContent = new ChunkedResearchContent(new string('x', 1_200_001));
        using var nativeHandler = new SignalHttpHandler(request =>
            request.RequestUri?.AbsolutePath == "/api/market/trading"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = nativeContent }
                : SignalHttpHandler.Json(JsonSerializer.Deserialize<JsonElement>(validBase))
        );
        using var nativeHttp = new HttpClient(nativeHandler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var fallback = JsonDocument.Parse(
            await new MarketResearchReader(nativeHttp).ReadForMarkets(
                new string('a', 64),
                ["ETH"],
                CancellationToken.None,
                true
            )
        );
        Check(
            fallback.RootElement.GetProperty("scan").GetProperty("symbols").GetArrayLength() == 0
                && !fallback.RootElement.GetProperty("nativeExecution").GetProperty("available").GetBoolean()
                && !fallback.RootElement.GetProperty("nativeExecution").GetProperty("executionAuthority").GetBoolean(),
            "native oversize retains healthy base with unavailable native evidence"
        );
        Check(nativeContent.BytesProduced < 1_200_001, "native response budget stops chunk production early");

        var signalContent = new ChunkedResearchContent(new string('x', 4_000_001));
        using var signalResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = signalContent };
        using var signalHandler = new SignalHttpHandler(_ => signalResponse);
        using var signalHttp = new HttpClient(signalHandler) { BaseAddress = new Uri("http://fixture.invalid") };
        await ExpectFailure(
            () => new MarketSignalReader(signalHttp, TimeProvider.System).Read(CancellationToken.None),
            "market signals reject before consuming the whole unknown-length body"
        );
        Check(signalContent.BytesProduced < 4_000_001, "signal response budget stops chunk production early");
        await MultibyteAllowance();
        await CallerCancellation("/api/market/research");
        await CallerCancellation("/api/market/trading");
        await CallerCancellation("/api/market/signals");
        await BodyDeadline("/api/market/research");
        await BodyDeadline("/api/market/trading");
        await BodyDeadline("/api/market/signals");
        Console.WriteLine(
            "Research transport budgets: unknown-length responses stop before full consumption and native fallback remains read-only."
        );
    }

    private static async Task MultibyteAllowance()
    {
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var research = JsonSerializer.Serialize(new { note = new string('ก', 39_980) }, options);
        var researchContent = new ChunkedResearchContent(research);
        using var researchHandler = new SignalHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = researchContent,
        });
        using var researchHttp = new HttpClient(researchHandler) { BaseAddress = new Uri("http://fixture.invalid") };
        Check(
            await new MarketResearchReader(researchHttp).Read(new string('a', 64), CancellationToken.None) == research
                && researchContent.BytesProduced == Encoding.UTF8.GetByteCount(research),
            "Valid multibyte research within the existing character allowance is retained exactly."
        );

        var time = new ManualTimeProvider();
        var signals = JsonSerializer.Serialize(
            new
            {
                available = false,
                checkedAt = time.GetUtcNow(),
                note = new string('ก', 999_000),
            },
            options
        );
        var signalContent = new ChunkedResearchContent(signals);
        using var signalHandler = new SignalHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = signalContent,
        });
        using var signalHttp = new HttpClient(signalHandler) { BaseAddress = researchHttp.BaseAddress };
        Check(
            await new MarketSignalReader(signalHttp, time).Read(CancellationToken.None) is null
                && signalContent.BytesProduced == Encoding.UTF8.GetByteCount(signals),
            "Valid multibyte signal evidence keeps its original character allowance and availability semantics."
        );
    }

    private static async Task CallerCancellation(string path)
    {
        using var cancellation = new CancellationTokenSource();
        var content = new ChunkedResearchContent(new string('x', 20_000), onChunk: cancellation.Cancel);
        using var handler = new SignalHttpHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri?.AbsolutePath == path ? content : new StringContent("{}"),
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        try
        {
            if (path == "/api/market/signals")
            {
                await new MarketSignalReader(http, TimeProvider.System).Read(cancellation.Token);
            }
            else
            {
                await new MarketResearchReader(http).ReadForMarkets(
                    new string('a', 64),
                    ["ETH"],
                    cancellation.Token,
                    path == "/api/market/trading"
                );
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Check(content.BytesProduced < 20_000, "Caller cancellation stops the current response early.");
            return;
        }

        throw new InvalidOperationException(
            "Caller cancellation must propagate instead of returning fallback evidence."
        );
    }

    private static async Task ExpectFailure(Func<Task> operation, string message)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or InvalidDataException or HttpRequestException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static async Task BodyDeadline(string path)
    {
        var content = new ChunkedResearchContent("{}", stall: true);
        using var handler = new SignalHttpHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri?.AbsolutePath == path ? content : new StringContent("{}"),
        });
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://fixture.invalid"),
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        try
        {
            if (path == "/api/market/signals")
            {
                await new MarketSignalReader(http, TimeProvider.System)
                    .Read(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                var result = await new MarketResearchReader(http)
                    .ReadForMarkets(new string('a', 64), ["ETH"], CancellationToken.None, path == "/api/market/trading")
                    .WaitAsync(TimeSpan.FromSeconds(5));
                using var document = JsonDocument.Parse(result);
                Check(
                    path == "/api/market/trading"
                        && !document.RootElement.GetProperty("nativeExecution").GetProperty("available").GetBoolean(),
                    "A native body timeout retains base research with unavailable native evidence."
                );
                return;
            }
        }
        catch (OperationCanceledException) when (path != "/api/market/trading")
        {
            return;
        }

        throw new InvalidOperationException("The configured HTTP deadline must also bound a stalled response body.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
