using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CoinSourceReaderTests
{
    [Fact]
    public async Task Reads_catalog_html_and_caches_for_24_hours()
    {
        using var folder = new TempFolder(); var clock = new FakeClock(TestTime);
        var bodies = new Queue<string>(["<html><main>visible <script>secret()</script><style>bad</style>" + new string('x', 120) + "</main></html>", "<html><main>refreshed " + new string('y', 120) + "</main></html>"]);
        var handler = new RecordingHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(bodies.Dequeue()) });
        var reader = new CoinSourceReader(new HttpClient(handler), folder.Path, clock);
        var first = await reader.ReadAsync("https://ethereum.org/whitepaper/", default);
        var second = await reader.ReadAsync("https://ethereum.org/whitepaper/", default);
        Assert.Equal(first, second); Assert.Equal(1, handler.Calls); Assert.Contains("visible", first.Text); Assert.DoesNotContain("secret", first.Text);
        var historicalPath = System.IO.Path.Combine(folder.Path, first.Source.Id + ".json");
        var historical = File.ReadAllBytes(historicalPath);
        clock.Advance(TimeSpan.FromHours(24)); var refreshed = await reader.ReadAsync("https://ethereum.org/whitepaper/", default); Assert.Equal(2, handler.Calls); Assert.NotEqual(first.Source.ContentHash, refreshed.Source.ContentHash);
        Assert.Equal(historical, File.ReadAllBytes(historicalPath));
        using var snapshot = System.Text.Json.JsonDocument.Parse(historical);
        var originalBytes = File.ReadAllBytes(System.IO.Path.Combine(folder.Path, snapshot.RootElement.GetProperty("rawHash").GetString() + ".bin"));
        Assert.Equal(snapshot.RootElement.GetProperty("rawHash").GetString(), Convert.ToHexStringLower(SHA256.HashData(originalBytes)));
        Assert.Equal(first.Text, snapshot.RootElement.GetProperty("document").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Requires_exact_catalog_and_pdf_magic_and_stream_bound()
    {
        using var folder = new TempFolder(); var clock = new FakeClock(TestTime);
        var catalogHandler = new RecordingHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-marker")) });
        var reader = new CoinSourceReader(new HttpClient(catalogHandler), folder.Path, clock, (b, _) => Task.FromResult("injected pdf marker " + new string('p', 120)));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync("https://example.com/private", default)); Assert.Equal(0, catalogHandler.Calls);
        var injected = await reader.ReadAsync(CoinSourceReader.Catalog[0].Url, default); Assert.Contains("injected pdf marker", injected.Text);
        using var pdfFolder = new TempFolder(); var pdfHandler = new RecordingHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("nope")) });
        await Assert.ThrowsAsync<InvalidDataException>(() => new CoinSourceReader(new HttpClient(pdfHandler), pdfFolder.Path, clock, (b, _) => Task.FromResult(new string('p', 120))).ReadAsync(CoinSourceReader.Catalog[0].Url, default));
        var huge = new StreamingContent(4 * 1024 * 1024 + 1); var hugeHandler = new RecordingHandler(_ => new(HttpStatusCode.OK) { Content = huge });
        await Assert.ThrowsAsync<InvalidDataException>(() => new CoinSourceReader(new HttpClient(hugeHandler), folder.Path, clock).ReadAsync(CoinSourceReader.Catalog[1].Url, default));
    }

    [Fact]
    public async Task Rejects_corrupted_snapshot_without_refetching_evidence()
    {
        using var folder = new TempFolder(); var clock = new FakeClock(TestTime); var bodies = new Queue<string>(["<main>" + new string('a', 140) + "</main>", "<main>" + new string('b', 140) + "</main>"]);
        var handler = new RecordingHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(bodies.Dequeue()) }); var reader = new CoinSourceReader(new HttpClient(handler), folder.Path, clock);
        var first = await reader.ReadAsync("https://ethereum.org/whitepaper/", default);
        var raw = Directory.GetFiles(folder.Path, "*.bin").Single(); File.WriteAllText(raw, "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync("https://ethereum.org/whitepaper/", default)); Assert.Equal(1, handler.Calls);
    }

    private sealed class FakeClock(DateTimeOffset value) : TimeProvider { private DateTimeOffset now = value; public override DateTimeOffset GetUtcNow() => now; public void Advance(TimeSpan value) => now += value; }
    private static readonly DateTimeOffset TestTime = new(2026, 10, 7, 3, 0, 0, TimeSpan.Zero);
    private sealed class TempFolder : IDisposable { public string Path { get; } = System.IO.Directory.CreateTempSubdirectory().FullName; public void Dispose() => Directory.Delete(Path, true); }
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler { public int Calls; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; var response = factory(request); response.RequestMessage = request; return Task.FromResult(response); } }
    private sealed class StreamingContent(int size) : HttpContent { protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) => await stream.WriteAsync(new byte[size]); protected override bool TryComputeLength(out long length) { length = -1; return false; } }
}
