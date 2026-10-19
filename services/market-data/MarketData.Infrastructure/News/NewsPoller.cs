using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quantara.MarketData.Domain.News;

namespace Quantara.MarketData.Infrastructure.News;

public sealed class NewsPoller : BackgroundService
{
    private readonly SqlNewsStore store;

    private readonly INewsTransport transport;

    private readonly TimeProvider clock;

    private readonly ILogger<NewsPoller>? logger;

    private static readonly Action<ILogger, Exception?> unavailable = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(707, "NewsStorageUnavailable"),
        "News ingestion unavailable; market monitoring continues independently."
    );

    public NewsPoller(
        SqlNewsStore store,
        INewsTransport transport,
        TimeProvider clock,
        ILogger<NewsPoller>? logger = null
    )
    {
        this.store = store;
        this.transport = transport;
        this.clock = clock;
        this.logger = logger;
    }

    private static readonly IReadOnlyList<NewsSource> sources =
    [
        new("federal-reserve", "Federal Reserve", new Uri("https://www.federalreserve.gov/feeds/press_all.xml")),
        new("bls", "U.S. Bureau of Labor Statistics", new Uri("https://www.bls.gov/feed/bls_latest.rss")),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                await this.store.Initialize(stoppingToken);
                await this.Poll(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                if (this.logger is { } log)
                {
                    NewsPoller.unavailable(log, null);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task Poll(CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            try
            {
                var validator = await this.store.ReadValidator(source.Id, cancellationToken);
                if (validator?.NextRetryAt is { } nextRetry && nextRetry > this.clock.GetUtcNow())
                {
                    continue;
                }

                var response = await this.transport.Fetch(
                    source.Url,
                    validator?.ETag,
                    validator?.LastModified,
                    cancellationToken
                );
                if (response.StatusCode == 304)
                {
                    var notModifiedAt = this.clock.GetUtcNow();
                    await this.store.Save(
                        source,
                        Array.Empty<NewsEvent>(),
                        new NewsSourceState(
                            source.Id,
                            source.Label,
                            source.Url.ToString(),
                            "NoNewEvents",
                            notModifiedAt,
                            null,
                            null
                        ),
                        response.ETag ?? validator?.ETag,
                        response.LastModified ?? validator?.LastModified,
                        cancellationToken
                    );
                    continue;
                }

                if (response.StatusCode == 429)
                {
                    var rateLimitedAt = this.clock.GetUtcNow();
                    var retryAt = System.Net.Http.Headers.RetryConditionHeaderValue.TryParse(
                        response.RetryAfter,
                        out var retry
                    )
                        ? retry.Date ?? rateLimitedAt.Add(retry.Delta ?? TimeSpan.FromMinutes(5))
                        : rateLimitedAt.AddMinutes(5);
                    if (retryAt <= rateLimitedAt)
                    {
                        retryAt = rateLimitedAt.AddMinutes(5);
                    }

                    await this.store.Save(
                        source,
                        Array.Empty<NewsEvent>(),
                        new NewsSourceState(
                            source.Id,
                            source.Label,
                            source.Url.ToString(),
                            "RateLimited",
                            null,
                            retryAt,
                            "Provider rate limit"
                        ),
                        response.ETag ?? validator?.ETag,
                        response.LastModified ?? validator?.LastModified,
                        cancellationToken
                    );
                    continue;
                }

                if (response.StatusCode is < 200 or >= 300 || response.Body is null)
                {
                    await this.store.Save(
                        source,
                        [],
                        new(
                            source.Id,
                            source.Label,
                            source.Url.ToString(),
                            "Unavailable",
                            null,
                            this.clock.GetUtcNow().AddMinutes(5),
                            "Provider response unavailable."
                        ),
                        validator?.ETag,
                        validator?.LastModified,
                        cancellationToken
                    );
                    continue;
                }

                var now = this.clock.GetUtcNow();
                var events = NewsRssParser.Parse(source, response.Body, now);
                await this.store.Save(
                    source,
                    events,
                    new NewsSourceState(source.Id, source.Label, source.Url.ToString(), "Healthy", now, null, null),
                    response.ETag,
                    response.LastModified,
                    cancellationToken
                );
            }
            catch (Exception exception)
                when (exception is HttpRequestException or InvalidDataException or System.Xml.XmlException
                    || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested
                )
            {
                await this.store.Save(
                    source,
                    Array.Empty<NewsEvent>(),
                    new NewsSourceState(
                        source.Id,
                        source.Label,
                        source.Url.ToString(),
                        exception is InvalidDataException or System.Xml.XmlException ? "ParseFailed" : "Unavailable",
                        null,
                        this.clock.GetUtcNow().AddMinutes(5),
                        "The source could not be read or validated; cached events retain their original age."
                    ),
                    null,
                    null,
                    cancellationToken
                );
            }
        }
    }
}
