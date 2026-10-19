namespace Quantara.MarketData.Application.News;

public sealed record NewsPilotStatus(
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastPollAt,
    bool Completed72Hours,
    int GapCount
);
