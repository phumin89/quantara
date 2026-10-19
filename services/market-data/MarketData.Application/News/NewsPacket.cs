using Quantara.MarketData.Domain.News;

namespace Quantara.MarketData.Application.News;

public sealed record NewsPacket(
    string Schema,
    DateTimeOffset ObservedAt,
    bool ExecutionAuthority,
    IReadOnlyList<NewsEvent> Events,
    IReadOnlyList<NewsSourceState> SourceStates,
    int OmittedEventCount,
    NewsPilotStatus Pilot
);
