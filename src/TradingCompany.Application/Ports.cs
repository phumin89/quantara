using TradingCompany.Domain;
namespace TradingCompany.Application;
public sealed record AgentModelRequest(
    string Role, string Model, string ReasoningEffort, string PromptVersion,
    string SystemInstructions, string InputJson, string JsonSchema,
    int MaxOutputTokens, TimeSpan Timeout);
public sealed record AgentModelResponse(
    string OutputJson, string ProviderRequestId, string Model,
    int InputTokens, int OutputTokens, TimeSpan Latency);
public interface ITradingAgentGateway
{
    Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken);
}
public interface IAiCompanyMeetingRunner
{
    bool Enabled { get; }
    string Model { get; }
    Task<AiCompanyMeetingResult> RunAsync(AiCompanyMeetingInput input, CancellationToken cancellationToken);
}
public interface IHistoricalMarketDataProvider { Task<IReadOnlyList<HistoricalBar>> GetAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken); }
public interface IRealtimeEventPublisher { Task PublishAsync(RealtimeEvent message, CancellationToken cancellationToken); }
public sealed record RealtimeEvent(string Type, Guid CorrelationId, DateTimeOffset OccurredAt, object Payload)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TradingAccountContext? AccountContext { get; init; }
}
public sealed record AgentPresentation(string CharacterId, string PreferredWorkstation, string DisplayRole);
public sealed record AgentArtifact(string Kind, string Title, string Summary, IReadOnlyDictionary<string, string> Facts);
public sealed record AgentRuntimeEventPayload(
    string AgentId,
    string Role,
    string Status,
    string Activity,
    string Location,
    string Summary,
    AgentPresentation? Presentation = null,
    AgentArtifact? Artifact = null,
    Guid? MeetingId = null,
    object? Result = null);
