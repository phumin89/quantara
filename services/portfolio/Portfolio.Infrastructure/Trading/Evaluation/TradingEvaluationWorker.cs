using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingEvaluationWorker(
    ITradingStore store,
    TradingEvaluationService evaluation,
    SemaphoreSlim gate,
    TimeProvider time,
    ILogger<TradingEvaluationWorker> logger
) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> failed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(2, "TradingEvaluationUnavailable"),
        "Read-only historical evaluation unavailable; execution remains independent."
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                await this.Review(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                TradingEvaluationWorker.failed(logger, null);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task Review(CancellationToken cancellationToken)
    {
        var snapshot = await store.Read(cancellationToken);
        var today = time.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).Date;
        if (
            snapshot.Rules.Version != "adaptive-intraday-v2"
            || snapshot.Autonomy is not { } autonomy
            || snapshot.Evaluation is { } previous
                && previous.Protocol == TradingReplayEvaluator.Protocol
                && previous.RulesHash == snapshot.RulesHash
                && previous.ObservedAt.ToOffset(TimeSpan.FromHours(7)).Date == today
        )
        {
            return;
        }

        var symbols = autonomy
            .Markets.OrderByDescending(item => item.Score)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .Select(item => item.Symbol)
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
        if (symbols.Length == 0)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var result = await evaluation.Evaluate(new(symbols, Rules: snapshot.Rules), timeout.Token);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await store.Read(cancellationToken);
            if (current.RulesHash == snapshot.RulesHash)
            {
                await store.Save(
                    current with
                    {
                        Revision = current.Revision + 1,
                        Evaluation = result,
                    },
                    current.Revision,
                    cancellationToken
                );
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
