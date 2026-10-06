using TradingCompany.Infrastructure;

namespace TradingCompany.LiveExecution;

public sealed class BitkubReceiptObserver(BitkubLiveBroker broker, BitkubOrderReceiptJournal journal) : BackgroundService
{
    private int ready;
    public bool Ready => Volatile.Read(ref ready) == 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await journal.CheckHealthyAsync(stoppingToken);
                await broker.ObserveOrdersAsync(journal.AppendAsync, value => Volatile.Write(ref ready, value ? 1 : 0), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            // Invalid/conflicting financial evidence stops this observer. A reconnect
            // is for a lost transport, never permission to ignore conflicting receipts.
            catch (InvalidDataException) { return; }
            catch { }
            Volatile.Write(ref ready, 0);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
