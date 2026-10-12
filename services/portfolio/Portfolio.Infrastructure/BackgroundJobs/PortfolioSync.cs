using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class PortfolioSync(
    IPortfolioRepository store,
    BitkubAccountReader exchange,
    IMarketDataClient marketData
) : BackgroundService, IPortfolioSync
{
    private readonly Channel<bool> requests = Channel.CreateBounded<bool>(1);

    private SyncStatus status = new(false, 0, 0, null);

    private int active;

    public SyncStatus Status => Volatile.Read(ref this.status);

    public bool Request()
    {
        if (Interlocked.CompareExchange(ref this.active, 1, 0) != 0)
        {
            return false;
        }

        Volatile.Write(ref this.status, new(true, 0, 0, null));
        if (this.requests.Writer.TryWrite(true))
        {
            return true;
        }

        Volatile.Write(ref this.status, this.Status with { Syncing = false });
        Interlocked.Exchange(ref this.active, 0);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.Request();
        await foreach (var request in this.requests.Reader.ReadAllAsync(stoppingToken))
        {
            Volatile.Write(ref this.status, new(true, 0, 0, null));
            try
            {
                await this.Import(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ExchangeReadException error)
            {
                Volatile.Write(ref this.status, this.Status with { Error = error.Message });
            }
            catch (Exception)
            {
                Volatile.Write(
                    ref this.status,
                    this.Status with
                    {
                        Error =
                            "Portfolio sync failed. Stored records are retained. Retry or check backend configuration.",
                    }
                );
            }
            finally
            {
                Volatile.Write(ref this.status, this.Status with { Syncing = false });
                Interlocked.Exchange(ref this.active, 0);
            }
        }
    }

    private async Task Import(CancellationToken cancellationToken)
    {
        var ledger = await store.Load(cancellationToken);
        var coins = await marketData.GetCoins(cancellationToken);
        var balances = await exchange.Balances(cancellationToken);
        ledger = ledger with { Balances = balances, UpdatedAt = DateTimeOffset.UtcNow };
        foreach (var balance in balances.Where(balance => balance.Symbol != "THB" && balance.Quantity > 0))
        {
            ledger = ledger.Remember(
                coins.FirstOrDefault(coin => coin.Symbol == balance.Symbol) ?? new(balance.Symbol, balance.Symbol),
                []
            );
        }

        await store.Save(ledger, cancellationToken);
        var prices = await marketData.GetQuotes(cancellationToken);
        ledger = ledger with { Prices = prices, PriceUpdatedAt = DateTimeOffset.UtcNow };
        await store.Save(ledger, cancellationToken);
        var exchangeCoins = coins.Where(coin => coin.Source == "exchange").ToArray();
        Volatile.Write(ref this.status, this.Status with { TotalSymbols = exchangeCoins.Length });
        var failures = 0;
        var unavailable = new List<string>();
        // Scan every THB market so sold-out coins can be discovered too.
        foreach (
            var coin in exchangeCoins.OrderByDescending(coin =>
                balances.Any(balance => balance.Symbol == coin.Symbol && balance.Quantity > 0)
            )
        )
        {
            try
            {
                var trades = await exchange.History(coin.Symbol, cancellationToken);
                if (trades.Length > 0)
                {
                    ledger = ledger.Remember(coin, trades);
                    await store.Save(ledger, cancellationToken);
                }
            }
            catch (ExchangeReadException error) when (error.Code == "61")
            {
                unavailable.Add(coin.Symbol);
            }
            catch (ExchangeReadException)
            {
                failures++;
            }

            Volatile.Write(ref this.status, this.Status with { CompletedSymbols = this.Status.CompletedSymbols + 1 });
        }

        if (failures == 0)
        {
            ledger = ledger with
            {
                HistoryUpdatedAt = DateTimeOffset.UtcNow,
                UnavailableHistorySymbols = unavailable.ToArray(),
            };
            await store.Save(ledger, cancellationToken);
        }
        else
        {
            Volatile.Write(
                ref this.status,
                this.Status with
                {
                    Error =
                        $"History could not be imported for {failures} markets. Stored records are retained; retry sync.",
                }
            );
        }
    }
}
