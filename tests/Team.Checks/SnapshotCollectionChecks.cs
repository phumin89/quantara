using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class SnapshotCollectionChecks
{
    public static async Task Run()
    {
        foreach (var scenario in new[] { "deadline", "caller", "stale", "future", "healthy", "unknown-cash" })
        {
            await Collection(scenario);
        }
    }

    private static async Task Collection(string scenario)
    {
        var time = new SnapshotCollectionTime();
        var startedAt = time.GetUtcNow();
        var walletAt = startedAt.AddSeconds(
            scenario == "stale" ? -59
            : scenario == "future" ? 4
            : -1
        );
        var account = new string('a', 64);
        var reachedOrders = false;
        using var caller = new CancellationTokenSource();
        using var handler = new SignalHttpHandler(request =>
        {
            Check(request.Method == HttpMethod.Get, "Collection is GET-only");
            Check(
                request.RequestUri?.GetLeftPart(UriPartial.Authority) == "http://snapshot.fixture.invalid",
                "Synthetic host only"
            );
            Check(!request.Headers.Contains("X-Quantara-Trading-Control"), "Research carries no control token");
            var address = request.RequestUri ?? throw new InvalidOperationException("Fixture URI is missing.");
            if (address.AbsolutePath == "/api/crypto/orders")
            {
                reachedOrders = true;
                Check(time.DeadlineDue == TimeSpan.FromSeconds(45), "One collection deadline is exactly 45 seconds");
                if (scenario == "deadline")
                {
                    time.Advance(TimeSpan.FromSeconds(45));
                    time.FireDeadline();
                }
                else if (scenario == "caller")
                {
                    caller.Cancel();
                }
                else
                {
                    time.Advance(TimeSpan.FromSeconds(scenario == "future" ? -2 : 2));
                }
            }

            return Reply(address, account, startedAt, walletAt, scenario == "unknown-cash");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://snapshot.fixture.invalid/") };
        var reader = new PortfolioSnapshotReader(http, null, time);
        string? collected = null;
        Exception? failure = null;
        try
        {
            collected = await reader.Read(caller.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException)
        {
            failure = error;
        }

        Check(reachedOrders, "The fixture reaches late order collection");
        if (scenario is "deadline" or "caller")
        {
            Check(
                failure is OperationCanceledException && collected is null,
                "Cancellation aborts the entire collection, never an optional fallback"
            );
            Check(
                caller.IsCancellationRequested == (scenario == "caller"),
                "Deadline and original caller cancellation stay distinct"
            );
            return;
        }

        if (scenario is "stale" or "future")
        {
            Check(
                failure is InvalidOperationException && collected is null,
                "Wallet freshness is rechecked before returning assembled evidence"
            );
            return;
        }

        Check(failure is null && collected is not null, "Healthy short collection succeeds");
        using var document = JsonDocument.Parse(
            collected ?? throw new InvalidOperationException("Snapshot is missing.")
        );
        var root = document.RootElement;
        var wallet = root.GetProperty("walletEvidence");
        Check(root.GetProperty("accountKey").GetString() == account, "Account is retained");
        Check(
            wallet.GetProperty("observedAt").GetDateTimeOffset() == walletAt,
            "Wallet source time is not collection completion"
        );
        foreach (var field in new[] { "updatedAt", "priceUpdatedAt", "historyUpdatedAt" })
        {
            Check(root.GetProperty(field).GetDateTimeOffset() == startedAt, "Portfolio source timestamp is unchanged");
        }

        var available = root.GetProperty("summary").GetProperty("availableThb");
        Check(
            scenario == "unknown-cash" ? available.ValueKind == JsonValueKind.Null : available.GetDecimal() == 700m,
            "Spendable cash preserves wallet availability and unknown semantics"
        );
        Check(
            root.GetProperty("summary").GetProperty("totalAssets").ValueKind == JsonValueKind.Null,
            "Unknown total remains null"
        );
        var position = root.GetProperty("positions")[0];
        foreach (var field in new[] { "currentPrice", "value", "averageCost" })
        {
            Check(position.GetProperty(field).ValueKind == JsonValueKind.Null, "Unknown position values remain null");
        }

        Check(position.GetProperty("costStatus").GetString() == "Unknown", "Unknown cost stays explicit");
        Check(
            root.GetProperty("openOrders").GetProperty("available").GetBoolean(),
            "Healthy order evidence remains available"
        );
    }

    private static HttpResponseMessage Reply(
        Uri address,
        string account,
        DateTimeOffset sourceAt,
        DateTimeOffset walletAt,
        bool unknownCash
    )
    {
        if (address.AbsolutePath != "/api/crypto/history")
        {
            Check(address.Query.Length == 0, "Only history has a query");
        }

        return address.AbsolutePath switch
        {
            "/api/crypto/account" => SignalHttpHandler.Json(new { accountKey = account }),
            "/api/crypto/account/evidence" => SignalHttpHandler.Json(
                new
                {
                    accountKey = account,
                    observedAt = walletAt,
                    balances = new[]
                    {
                        new
                        {
                            symbol = "THB",
                            quantity = 1000m,
                            available = unknownCash ? (decimal?)null : 700m,
                        },
                        new
                        {
                            symbol = "BTC",
                            quantity = 1m,
                            available = (decimal?)null,
                        },
                    },
                    fee = new { rate = 0.0025m, status = "estimated" },
                }
            ),
            "/api/crypto/portfolio" => SignalHttpHandler.Json(
                new
                {
                    updatedAt = sourceAt,
                    priceUpdatedAt = sourceAt,
                    historyUpdatedAt = sourceAt,
                    historyCoverage = "Synthetic retained history",
                    error = (string?)null,
                    syncing = false,
                    positions = new[]
                    {
                        new
                        {
                            symbol = "BTC",
                            quantity = 1m,
                            currentPrice = (decimal?)null,
                            value = (decimal?)null,
                            averageCost = (decimal?)null,
                            costStatus = "Unknown",
                        },
                    },
                    summary = new
                    {
                        totalAssets = (decimal?)null,
                        availableThb = 9999m,
                        holdingCount = 1,
                    },
                }
            ),
            "/api/crypto/history" when address.Query == "?coin=BTC" => SignalHttpHandler.Json(
                new { trades = Array.Empty<object>(), coverage = "Synthetic history" }
            ),
            "/api/trading" => SignalHttpHandler.Json(new { accountKey = account, mode = "paper" }),
            "/api/crypto/orders" => SignalHttpHandler.Json(new { proposals = Array.Empty<object>() }),
            _ => throw new InvalidOperationException("Unexpected synthetic snapshot route: " + address.PathAndQuery),
        };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Snapshot collection check failed: " + message);
        }
    }
}
