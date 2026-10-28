using System.Net;
using System.Text;
using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchIngressTransportChecks
{
    public static async Task Run()
    {
        await AccountValidation();
        await OversizedAccount();
        foreach (var news in new[] { true, false })
        {
            await CallerCancellation(news);
            await BodyDeadline(news);
        }

        Console.WriteLine(
            "Research ingress: bounded account bodies, exact account validation, caller cancellation and news/account body deadlines passed."
        );
    }

    private static async Task AccountValidation()
    {
        var account = string.Concat(Enumerable.Repeat("aBcD0123", 8));
        using (var handler = Handler(new StringContent(JsonSerializer.Serialize(new { accountKey = account })), false))
        using (var http = Client(handler))
        {
            Check(
                await new CurrentAccountReader(http).Read(CancellationToken.None) == account,
                "A verified 64-hex account must be preserved exactly."
            );
            Check(handler.Requests == 1, "Account validation uses one synthetic read.");
        }

        foreach (var invalid in new string?[] { null, "", new string('a', 63), new string('g', 64) })
        {
            using var handler = Handler(
                new StringContent(JsonSerializer.Serialize(new { accountKey = invalid })),
                false
            );
            using var http = Client(handler);
            var rejected = false;
            try
            {
                await new CurrentAccountReader(http).Read(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Check(
                rejected && handler.Requests == 1,
                "Missing or malformed account identity must be rejected without retry."
            );
        }
    }

    private static async Task OversizedAccount()
    {
        var body = JsonSerializer.Serialize(new { accountKey = new string('a', 64) }) + new string(' ', 20_000);
        var content = new ChunkedResearchContent(body);
        using var handler = Handler(content, false);
        using var http = Client(handler);
        Check(content.Headers.ContentLength is null, "The oversized account fixture has no Content-Length header.");
        var rejected = false;
        try
        {
            await new CurrentAccountReader(http).Read(CancellationToken.None);
        }
        catch (HttpRequestException)
        {
            rejected = true;
        }

        Check(
            rejected && content.BytesProduced <= 16_000 && content.BytesProduced < Encoding.UTF8.GetByteCount(body),
            "The account byte budget must stop chunk production before the entire valid JSON response is buffered."
        );
        Check(handler.Requests == 1, "An oversized account response must not be retried.");
    }

    private static async Task CallerCancellation(bool news)
    {
        using var cancellation = new CancellationTokenSource();
        var body = "{}" + new string(' ', 20_000);
        var content = new ChunkedResearchContent(body, onChunk: cancellation.Cancel);
        using var handler = Handler(content, news);
        using var http = Client(handler);
        var propagated = false;
        try
        {
            await Read(http, news, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            propagated = true;
        }

        Check(
            propagated && content.BytesProduced > 0 && content.BytesProduced < Encoding.UTF8.GetByteCount(body),
            "Caller cancellation after a body chunk must propagate instead of returning partial or fallback evidence."
        );
        Check(handler.Requests == 1, "A cancelled ingress request must not be retried.");
    }

    private static async Task BodyDeadline(bool news)
    {
        using var cleanup = new CancellationTokenSource();
        var content = new ChunkedResearchContent("{}", stall: true);
        using var handler = Handler(content, news);
        using var http = Client(handler);
        http.Timeout = TimeSpan.FromMilliseconds(200);
        var pending = Read(http, news, cleanup.Token);
        var deadlineCancelled = false;
        try
        {
            var body = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Check(news, "A stalled account body must cancel instead of returning an account.");
            using var document = JsonDocument.Parse(body);
            var packet = document.RootElement;
            Check(
                packet.GetProperty("schema").GetString() == "news-v1"
                    && !packet.GetProperty("executionAuthority").GetBoolean()
                    && packet.GetProperty("events").GetArrayLength() == 0
                    && packet.GetProperty("sourceStates")[0].GetProperty("reason").GetString() == "source_unavailable",
                "A news body deadline must retain the existing safe unavailable packet."
            );
            deadlineCancelled = true;
        }
        catch (OperationCanceledException) when (!news && !cleanup.IsCancellationRequested)
        {
            deadlineCancelled = true;
        }
        finally
        {
            // Also releases the old unbounded-body path if the observation guard fails.
            await cleanup.CancelAsync();
            try
            {
                await pending;
            }
            catch (OperationCanceledException) when (cleanup.IsCancellationRequested) { }
        }

        Check(
            deadlineCancelled && handler.Requests == 1,
            "The configured HTTP deadline must bound body consumption after synthetic headers arrive."
        );
    }

    private static Task<string> Read(HttpClient http, bool news, CancellationToken cancellationToken) =>
        news
            ? new NewsEvidenceReader(http).Read(null, cancellationToken)
            : new CurrentAccountReader(http).Read(cancellationToken);

    private static HttpClient Client(SignalHttpHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://research.fixture.invalid/") };

    private static SignalHttpHandler Handler(HttpContent content, bool news) =>
        new(request =>
        {
            var address =
                request.RequestUri ?? throw new InvalidOperationException("Synthetic ingress address missing.");
            Check(
                request.Method == HttpMethod.Get
                    && address.Scheme == "http"
                    && address.Host == "research.fixture.invalid"
                    && address.AbsolutePath == (news ? "/api/market/news" : "/api/crypto/account")
                    && address.Query.Length == 0,
                "Ingress fixtures accept only their exact synthetic GET."
            );
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
