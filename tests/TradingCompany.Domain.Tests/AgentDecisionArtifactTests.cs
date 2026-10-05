using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class AgentDecisionArtifactTests
{
    [Fact]
    public void Key_is_valid_by_construction()
    {
        Assert.Throws<ArgumentException>(() => Key(model: " "));
        Assert.Throws<ArgumentException>(() => Key(promptVersion: ""));
        Assert.Throws<ArgumentException>(() => Key(role: "\t"));
        Assert.Throws<ArgumentException>(() => Key(evidenceHash: ""));
        Assert.Throws<ArgumentException>(() => Key(mandateVersion: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Key(dataCutoff: default(DateOnly)));
    }

    [Fact]
    public void Artifact_is_valid_by_construction()
    {
        Assert.Throws<ArgumentException>(() => Artifact(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Artifact(createdAt: default(DateTimeOffset)));
        Assert.Throws<ArgumentException>(() => Artifact(outputJson: "not-json"));
        Assert.Throws<ArgumentException>(() => Artifact(providerRequestId: " "));
        Assert.Throws<ArgumentException>(() => Artifact(providerModel: ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => Artifact(inputTokens: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Artifact(outputTokens: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Artifact(latency: TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public async Task Append_creates_one_canonical_file_and_lookup_returns_it()
    {
        await using var fixture = new StoreFixture();
        var artifact = Artifact();
        await fixture.Store.AppendAsync(artifact, CancellationToken.None);

        Assert.Equal(artifact, await fixture.Store.FindEarliestAsync(artifact.Key, CancellationToken.None));
        var file = Assert.Single(Directory.GetFiles(fixture.Root));
        Assert.Equal($"{artifact.ArtifactId:D}.json", Path.GetFileName(file));
        var json = await File.ReadAllTextAsync(file);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("artifactId", out _));
        Assert.DoesNotContain(Environment.NewLine, json, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "index.json")));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("prompt")]
    [InlineData("role")]
    [InlineData("hash")]
    [InlineData("mandate")]
    [InlineData("cutoff")]
    public async Task Lookup_requires_each_of_the_six_key_fields_to_match(string field)
    {
        await using var fixture = new StoreFixture();
        var artifact = Artifact();
        await fixture.Store.AppendAsync(artifact, CancellationToken.None);
        var mismatch = field switch
        {
            "model" => Key(model: "other"),
            "prompt" => Key(promptVersion: "other"),
            "role" => Key(role: "other"),
            "hash" => Key(evidenceHash: "other"),
            "mandate" => Key(mandateVersion: "other"),
            _ => Key(dataCutoff: new DateOnly(2025, 1, 2)),
        };

        Assert.Null(await fixture.Store.FindEarliestAsync(mismatch, CancellationToken.None));
    }

    [Fact]
    public async Task Multiple_versions_return_earliest_created_time()
    {
        await using var fixture = new StoreFixture();
        var later = Artifact(createdAt: new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var earlier = Artifact(createdAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await fixture.Store.AppendAsync(later, CancellationToken.None);
        await fixture.Store.AppendAsync(earlier, CancellationToken.None);
        Assert.Equal(earlier, await fixture.Store.FindEarliestAsync(Key(), CancellationToken.None));
    }

    [Fact]
    public async Task Equal_created_times_use_artifact_id_as_deterministic_tiebreaker()
    {
        await using var fixture = new StoreFixture();
        var time = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var high = Artifact(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), time);
        var low = Artifact(Guid.Parse("00000000-0000-0000-0000-000000000001"), time);
        await fixture.Store.AppendAsync(high, CancellationToken.None);
        await fixture.Store.AppendAsync(low, CancellationToken.None);
        Assert.Equal(low, await fixture.Store.FindEarliestAsync(Key(), CancellationToken.None));
    }

    [Fact]
    public async Task Same_id_never_overwrites_existing_artifact()
    {
        await using var fixture = new StoreFixture();
        var first = Artifact();
        await fixture.Store.AppendAsync(first, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => fixture.Store.AppendAsync(
            Artifact(first.ArtifactId, outputJson: "{\"different\":true}"), CancellationToken.None));
        Assert.Equal(first, await fixture.Store.FindEarliestAsync(first.Key, CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_skips_corrupt_filename_mismatch_and_invalid_content()
    {
        await using var fixture = new StoreFixture();
        var valid = Artifact(createdAt: new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await fixture.Store.AppendAsync(valid, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, $"{Guid.NewGuid():D}.json"), "{");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "not-a-guid.json"), "{}");
        var mismatch = Artifact(createdAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, $"{Guid.NewGuid():D}.json"), JsonSerializer.Serialize(mismatch));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, $"{Guid.NewGuid():D}.json"), "{\"artifactId\":\"00000000-0000-0000-0000-000000000000\"}");
        Assert.Equal(valid, await fixture.Store.FindEarliestAsync(Key(), CancellationToken.None));
    }

    [Fact]
    public async Task Precancelled_append_creates_no_final_or_temporary_file()
    {
        await using var fixture = new StoreFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.AppendAsync(Artifact(), cancellation.Token));
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task Concurrent_appends_leave_only_complete_parseable_final_files()
    {
        await using var fixture = new StoreFixture();
        var artifacts = Enumerable.Range(0, 32).Select(i => Artifact(
            createdAt: new DateTimeOffset(2025, 1, 1, 0, 0, i, TimeSpan.Zero))).ToArray();
        await Task.WhenAll(artifacts.Select(x => fixture.Store.AppendAsync(x, CancellationToken.None)));
        var files = Directory.GetFiles(fixture.Root);
        Assert.Equal(artifacts.Length, files.Length);
        Assert.All(files, file =>
        {
            Assert.EndsWith(".json", file, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        });
        Assert.Equal(artifacts[0], await fixture.Store.FindEarliestAsync(Key(), CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_propagates_cancellation()
    {
        await using var fixture = new StoreFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.FindEarliestAsync(Key(), cancellation.Token));
    }

    [Fact]
    public void Store_requires_an_absolute_root() =>
        Assert.Throws<ArgumentException>(() => new JsonAgentDecisionArtifactStore("relative-path"));

    private static AgentArtifactKey Key(
        string model = "gpt-test", string promptVersion = "exit-v1", string role = "PortfolioExitAgent",
        string evidenceHash = "abc123", string mandateVersion = "mandate-v1", DateOnly? dataCutoff = null) =>
        new(model, promptVersion, role, evidenceHash, mandateVersion, dataCutoff ?? new DateOnly(2025, 1, 1));

    private static AgentDecisionArtifact Artifact(
        Guid? id = null, DateTimeOffset? createdAt = null, string outputJson = "{}", string providerRequestId = "resp_1",
        string providerModel = "gpt-test", int inputTokens = 10, int outputTokens = 20, TimeSpan? latency = null) =>
        new(id ?? Guid.NewGuid(), Key(), createdAt ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            outputJson, providerRequestId, providerModel, inputTokens, outputTokens, latency ?? TimeSpan.FromMilliseconds(50));

    private sealed class StoreFixture : IAsyncDisposable
    {
        public StoreFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "quantara-artifacts", Guid.NewGuid().ToString("N"));
            Store = new JsonAgentDecisionArtifactStore(Root);
        }
        public string Root { get; }
        public JsonAgentDecisionArtifactStore Store { get; }
        public ValueTask DisposeAsync()
        {
            Store.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            return ValueTask.CompletedTask;
        }
    }
}
