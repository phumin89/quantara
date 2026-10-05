using System.Text.Json;
using TradingCompany.Application;

namespace TradingCompany.Infrastructure;

public sealed class JsonAgentDecisionArtifactStore : IAgentDecisionArtifactStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly string rootDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);

    public JsonAgentDecisionArtifactStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathFullyQualified(rootDirectory))
            throw new ArgumentException("Artifact root must be an absolute path.", nameof(rootDirectory));

        this.rootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        Directory.CreateDirectory(this.rootDirectory);
    }

    public async Task<AgentDecisionArtifact> AppendAsync(
        AgentDecisionArtifact artifact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var finalPath = ResolveFile($"{artifact.ArtifactId:D}.json");
            temporaryPath = ResolveFile($".{artifact.ArtifactId:D}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, artifact, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath, overwrite: false);
            temporaryPath = null;
            return artifact;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            gate.Release();
        }
    }

    public async Task<AgentDecisionArtifact?> FindEarliestAsync(
        AgentArtifactKey key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var matches = new List<AgentDecisionArtifact>();
            foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out var filenameId)) continue;

                try
                {
                    await using var stream = new FileStream(
                        path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
                    var artifact = await JsonSerializer.DeserializeAsync<AgentDecisionArtifact>(
                        stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                    if (artifact is not null && artifact.ArtifactId == filenameId && artifact.Key == key)
                        matches.Add(artifact);
                }
                catch (JsonException) { }
                catch (IOException) { }
                catch (ArgumentException) { }
            }

            return matches
                .OrderBy(artifact => artifact.CreatedAt)
                .ThenBy(artifact => artifact.ArtifactId)
                .FirstOrDefault();
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private string ResolveFile(string fileName)
    {
        if (Path.GetFileName(fileName) != fileName)
            throw new IOException("Unsafe artifact file name.");

        var path = Path.GetFullPath(Path.Combine(rootDirectory, fileName));
        var relative = Path.GetRelativePath(rootDirectory, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new IOException("Unsafe artifact path.");
        return path;
    }
}
