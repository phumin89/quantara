using System.Text.Json;

namespace TradingCompany.Application;

public sealed record AgentArtifactKey
{
    public AgentArtifactKey(
        string model,
        string promptVersion,
        string role,
        string evidenceHash,
        string mandateVersion,
        DateOnly dataCutoff)
    {
        Model = Required(model, nameof(model));
        PromptVersion = Required(promptVersion, nameof(promptVersion));
        Role = Required(role, nameof(role));
        EvidenceHash = Required(evidenceHash, nameof(evidenceHash));
        MandateVersion = Required(mandateVersion, nameof(mandateVersion));
        if (dataCutoff == default) throw new ArgumentOutOfRangeException(nameof(dataCutoff));
        DataCutoff = dataCutoff;
    }

    public string Model { get; }
    public string PromptVersion { get; }
    public string Role { get; }
    public string EvidenceHash { get; }
    public string MandateVersion { get; }
    public DateOnly DataCutoff { get; }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Artifact key field is required.", parameterName)
            : value;
}

public sealed record AgentDecisionArtifact
{
    public AgentDecisionArtifact(
        Guid artifactId,
        AgentArtifactKey key,
        DateTimeOffset createdAt,
        string outputJson,
        string providerRequestId,
        string providerModel,
        int inputTokens,
        int outputTokens,
        TimeSpan latency)
    {
        if (artifactId == Guid.Empty) throw new ArgumentException("Artifact ID is required.", nameof(artifactId));
        ArgumentNullException.ThrowIfNull(key);
        if (createdAt == default) throw new ArgumentException("Created time is required.", nameof(createdAt));
        ValidateJson(outputJson);
        if (string.IsNullOrWhiteSpace(providerRequestId)) throw new ArgumentException("Provider request ID is required.", nameof(providerRequestId));
        if (string.IsNullOrWhiteSpace(providerModel)) throw new ArgumentException("Provider model is required.", nameof(providerModel));
        if (inputTokens < 0) throw new ArgumentOutOfRangeException(nameof(inputTokens));
        if (outputTokens < 0) throw new ArgumentOutOfRangeException(nameof(outputTokens));
        if (latency < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(latency));

        ArtifactId = artifactId;
        Key = key;
        CreatedAt = createdAt;
        OutputJson = outputJson;
        ProviderRequestId = providerRequestId;
        ProviderModel = providerModel;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Latency = latency;
    }

    public Guid ArtifactId { get; }
    public AgentArtifactKey Key { get; }
    public DateTimeOffset CreatedAt { get; }
    public string OutputJson { get; }
    public string ProviderRequestId { get; }
    public string ProviderModel { get; }
    public int InputTokens { get; }
    public int OutputTokens { get; }
    public TimeSpan Latency { get; }

    private static void ValidateJson(string outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson))
            throw new ArgumentException("Output JSON is required.", nameof(outputJson));
        try
        {
            using var document = JsonDocument.Parse(outputJson);
        }
        catch (JsonException)
        {
            throw new ArgumentException("Output must be valid JSON.", nameof(outputJson));
        }
    }
}

public interface IAgentDecisionArtifactStore
{
    Task<AgentDecisionArtifact> AppendAsync(AgentDecisionArtifact artifact, CancellationToken cancellationToken);
    Task<AgentDecisionArtifact?> FindEarliestAsync(AgentArtifactKey key, CancellationToken cancellationToken);
}

public sealed class AgentModelUnavailableException(string message) : InvalidOperationException(message);
public sealed class AgentQuotaUnavailableException(string message) : InvalidOperationException(message);
public sealed class AgentProviderException(string message) : InvalidOperationException(message);
public sealed class AgentResponseFormatException(string message) : FormatException(message);
public sealed class AgentProviderTimeoutException(string message) : TimeoutException(message);
