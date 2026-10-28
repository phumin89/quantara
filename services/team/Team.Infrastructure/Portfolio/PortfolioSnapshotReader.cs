using System.Text.Encodings.Web;
using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class PortfolioSnapshotReader : IPortfolioSnapshotReader
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient http;

    private readonly IMarketResearchReader? marketResearchReader;

    private readonly TimeProvider time;

    public PortfolioSnapshotReader(
        HttpClient http,
        IMarketResearchReader? marketResearchReader = null,
        TimeProvider? time = null
    )
    {
        this.http = http;
        this.marketResearchReader = marketResearchReader;
        this.time = time ?? TimeProvider.System;
    }

    public Task<string> Read(CancellationToken cancellationToken) => this.ReadForMarkets([], cancellationToken);

    public async Task<string> ReadForMarkets(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45), this.time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        return await this.Collect(symbols, linked.Token);
    }

    private async Task<string> Collect(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        var accountKey = await new CurrentAccountReader(this.http).Read(cancellationToken);
        var wallet = await this.ReadWalletEvidence(accountKey, cancellationToken);
        using var response = await this.http.GetAsync("api/crypto/portfolio", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (
            root.GetProperty("updatedAt").ValueKind == JsonValueKind.Null
            || root.GetProperty("error").ValueKind != JsonValueKind.Null
            || root.GetProperty("syncing").GetBoolean()
        )
        {
            throw new InvalidOperationException(
                "A reconciled portfolio snapshot is unavailable. Refresh Crypto and retry."
            );
        }

        var positions = root.GetProperty("positions")
            .EnumerateArray()
            .Select(item => new
            {
                symbol = item.GetProperty("symbol").GetString(),
                quantity = item.GetProperty("quantity").GetDecimal(),
                currentPrice = PortfolioSnapshotJsonProjection.OptionalDecimal(item, "currentPrice"),
                value = PortfolioSnapshotJsonProjection.OptionalDecimal(item, "value"),
                averageCost = PortfolioSnapshotJsonProjection.OptionalDecimal(item, "averageCost"),
                costStatus = item.GetProperty("costStatus").GetString(),
            })
            .ToArray();
        var histories = await this.ReadHistories(
            positions
                .Select(item => item.symbol)
                .OfType<string>()
                .Where(symbol => symbol.Length is > 0 and <= 30 && symbol.All(char.IsAsciiLetterOrDigit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10),
            cancellationToken
        );
        var summary = root.GetProperty("summary");
        if (await new CurrentAccountReader(this.http).Read(cancellationToken) != accountKey)
        {
            throw new InvalidOperationException("Account changed while collecting research evidence.");
        }

        var tradingOverview = await this.ReadTradingMode(accountKey, cancellationToken);
        var adaptiveTimeframes =
            tradingOverview is { } overview
            && TradingSnapshotProjection.TradingMode(tradingOverview) != "manualReview"
            && TradingSnapshotProjection.TradingStrategyVersion(overview)
                is "adaptive-intraday-v1"
                    or "adaptive-intraday-v2";
        var marketResearch = await this.ReadMarketResearch(accountKey, symbols, adaptiveTimeframes, cancellationToken);
        var openOrders = await this.ReadOpenOrders(cancellationToken);
        if (await new CurrentAccountReader(this.http).Read(cancellationToken) != accountKey)
        {
            throw new InvalidOperationException("Account changed while collecting order and market evidence.");
        }

        var json = JsonSerializer.Serialize(
            new
            {
                accountKey,
                tradingMode = TradingSnapshotProjection.TradingMode(tradingOverview),
                policyHash = tradingOverview is { } policyOverview
                && policyOverview.TryGetProperty("rulesHash", out var policyHash)
                    ? policyHash.GetString()
                    : null,
                rules = tradingOverview is { } rulesOverview && rulesOverview.TryGetProperty("rules", out var rules)
                    ? rules.Clone()
                    : (JsonElement?)null,
                aiPolicy = tradingOverview is { } aiPolicyOverview
                && aiPolicyOverview.TryGetProperty("aiPolicy", out var aiPolicy)
                    ? aiPolicy.Clone()
                    : (JsonElement?)null,
                objective = tradingOverview is { } goalOverview
                && goalOverview.TryGetProperty("objective", out var goal)
                    ? goal.Clone()
                    : (JsonElement?)null,
                tradingStrategy = TradingSnapshotProjection.TradingStrategy(tradingOverview),
                tradingPerformance = TradingSnapshotProjection.TradingPerformance(tradingOverview),
                walletEvidence = wallet.Evidence,
                positions,
                summary = new
                {
                    totalAssets = PortfolioSnapshotJsonProjection.OptionalDecimal(summary, "totalAssets"),
                    availableThb = wallet.AvailableThb,
                    holdingCount = PortfolioSnapshotJsonProjection.OptionalDecimal(summary, "holdingCount"),
                    currency = "THB",
                    totalAssetsDefinition = "Market value of coin positions only; available THB cash is reported separately and is excluded.",
                    availableThbDefinition = "Available spendable THB in the connected Bitkub account; excludes reserved THB and is not total cash.",
                    holdingCountDefinition = "Number of non-THB coins with a positive wallet quantity.",
                },
                price = new
                {
                    currency = "THB",
                    source = "Bitkub",
                    meaning = "Latest imported ticker price; it is not an execution quote and does not include spread or fees.",
                },
                history = histories,
                historySymbolsTruncated = positions.Length > 10,
                pairDefinition = "Each coin symbol is quoted against THB (for example BTC/THB).",
                updatedAt = root.GetProperty("updatedAt").Clone(),
                priceUpdatedAt = root.GetProperty("priceUpdatedAt").Clone(),
                historyUpdatedAt = root.GetProperty("historyUpdatedAt").Clone(),
                historyCoverage = root.GetProperty("historyCoverage").GetString(),
                marketResearch,
                openOrders,
            },
            PortfolioSnapshotReader.jsonOptions
        );
        if (json.Length > (adaptiveTimeframes ? 60000 : 40000))
        {
            throw new InvalidOperationException("Portfolio snapshot is too large.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        this.ValidateWalletTime(wallet.Evidence.GetProperty("observedAt").GetDateTimeOffset());
        return json;
    }

    private void ValidateWalletTime(DateTimeOffset observedAt)
    {
        var age = this.time.GetUtcNow() - observedAt;
        if (age > TimeSpan.FromSeconds(60) || age < TimeSpan.FromSeconds(-5))
        {
            throw new InvalidOperationException("Wallet evidence is stale or from the future.");
        }
    }

    private async Task<(JsonElement Evidence, decimal? AvailableThb)> ReadWalletEvidence(
        string accountKey,
        CancellationToken cancellationToken
    )
    {
        using var response = await this.http.GetAsync("api/crypto/account/evidence", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.GetProperty("accountKey").GetString() != accountKey)
        {
            throw new InvalidOperationException("Wallet evidence belongs to a different account.");
        }

        var observedAt = root.GetProperty("observedAt").GetDateTimeOffset();
        this.ValidateWalletTime(observedAt);

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relevantBalances = new List<JsonElement>();
        decimal? availableThb = null;
        foreach (var balance in root.GetProperty("balances").EnumerateArray())
        {
            var symbol = balance.GetProperty("symbol").GetString();
            var quantity = balance.GetProperty("quantity").GetDecimal();
            var available = PortfolioSnapshotJsonProjection.OptionalDecimal(balance, "available");
            if (
                string.IsNullOrWhiteSpace(symbol)
                || symbol.Length > 30
                || !symbol.All(char.IsAsciiLetterOrDigit)
                || !symbols.Add(symbol)
                || quantity < 0
                || (available is not null && (available < 0 || available > quantity))
            )
            {
                throw new InvalidOperationException("Wallet evidence contains an invalid balance.");
            }

            if (symbol.Equals("THB", StringComparison.OrdinalIgnoreCase))
            {
                availableThb = available;
            }

            if (quantity > 0 || symbol.Equals("THB", StringComparison.OrdinalIgnoreCase))
            {
                relevantBalances.Add(
                    JsonSerializer.SerializeToElement(
                        new
                        {
                            symbol,
                            quantity,
                            available,
                        }
                    )
                );
            }
        }

        return (
            JsonSerializer.SerializeToElement(
                new
                {
                    accountKey,
                    observedAt,
                    balances = relevantBalances,
                    sourceBalanceCount = symbols.Count,
                    omittedZeroBalanceCount = symbols.Count - relevantBalances.Count,
                    fee = root.GetProperty("fee").Clone(),
                    sizingProfile = root.TryGetProperty("sizingProfile", out var sizingProfile)
                        ? sizingProfile.GetString()
                        : "disabled",
                    riskPolicyHash = root.TryGetProperty("riskPolicyHash", out var riskPolicyHash)
                        ? riskPolicyHash.GetString()
                        : null,
                }
            ),
            availableThb
        );
    }

    public async Task EnsureReady(CancellationToken cancellationToken)
    {
        using var response = await this.http.GetAsync("api/crypto/portfolio", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
        var root = document.RootElement;
        if (
            root.GetProperty("updatedAt").ValueKind == JsonValueKind.Null
            || root.GetProperty("error").ValueKind != JsonValueKind.Null
            || root.GetProperty("syncing").GetBoolean()
        )
        {
            throw new InvalidOperationException("Verified portfolio data is not ready.");
        }

        await new CurrentAccountReader(this.http).Read(cancellationToken);
    }

    private async Task<object> ReadMarketResearch(
        string accountKey,
        IReadOnlyList<string> symbols,
        bool adaptiveTimeframes,
        CancellationToken cancellationToken
    )
    {
        if (this.marketResearchReader is null)
        {
            return new { available = false, reason = "readerUnavailable" };
        }

        try
        {
            using var research = JsonDocument.Parse(
                await this.marketResearchReader.ReadForMarkets(
                    accountKey,
                    symbols,
                    cancellationToken,
                    adaptiveTimeframes
                )
            );
            return research.RootElement.Clone();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new { available = false, reason = "unavailable" };
        }
    }

    private async Task<object> ReadOpenOrders(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await this.http.GetAsync("api/crypto/orders", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var orders = document
                .RootElement.GetProperty("proposals")
                .EnumerateArray()
                .Where(order =>
                    order.GetProperty("status").GetString()
                        is "pending"
                            or "submitted"
                            or "partiallyFilled"
                            or "submitting"
                            or "unknown"
                )
                .ToArray();
            return new
            {
                available = true,
                observedAt = this.time.GetUtcNow(),
                returnedOrderCount = Math.Min(orders.Length, 10),
                truncated = orders.Length > 10 || document.RootElement.GetProperty("proposals").GetArrayLength() >= 50,
                coverage = "At most 50 recent/prioritized account proposals were queried; at most 10 active proposals are included. Pending review is not a submitted broker order.",
                orders = orders
                    .Take(10)
                    .Select(order => new
                    {
                        id = order.GetProperty("id").Clone(),
                        symbol = order.GetProperty("symbol").Clone(),
                        side = order.GetProperty("side").Clone(),
                        amount = order.GetProperty("amount").Clone(),
                        limitPrice = order.GetProperty("limitPrice").Clone(),
                        status = order.GetProperty("status").Clone(),
                        reason = order.GetProperty("reason").Clone(),
                    })
                    .ToArray(),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new
            {
                available = false,
                reason = "Open order evidence is unavailable; do not assume there are no active orders.",
            };
        }
    }

    private async Task<JsonElement?> ReadTradingMode(string accountKey, CancellationToken cancellationToken)
    {
        JsonElement overview;
        try
        {
            using var response = await this.http.GetAsync("api/trading", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken
            );
            overview = document.RootElement.Clone();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        if (overview.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (
            !overview.TryGetProperty("accountKey", out var account)
            || account.ValueKind != JsonValueKind.String
            || account.GetString() != accountKey
        )
        {
            throw new InvalidOperationException("Trading mode evidence belongs to a different or unknown account.");
        }

        return overview;
    }

    private async Task<object[]> ReadHistories(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        var histories = new List<object>();
        foreach (var symbol in symbols)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var response = await this.http.GetAsync(
                    $"api/crypto/history?coin={Uri.EscapeDataString(symbol)}",
                    timeout.Token
                );
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
                var root = document.RootElement;
                var sourceTrades = root.GetProperty("trades")
                    .EnumerateArray()
                    .Take(20)
                    .Select(PortfolioSnapshotJsonProjection.ToTradeEvidence)
                    .ToArray();
                histories.Add(
                    new
                    {
                        symbol,
                        available = true,
                        coverage = root.GetProperty("coverage").GetString(),
                        returnedTradeCount = sourceTrades.Length,
                        truncated = root.GetProperty("trades").GetArrayLength() > sourceTrades.Length,
                        trades = sourceTrades,
                    }
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                histories.Add(
                    new
                    {
                        symbol,
                        available = false,
                        coverage = (string?)null,
                        returnedTradeCount = 0,
                        truncated = false,
                        trades = Array.Empty<object>(),
                    }
                );
            }
        }

        return histories.ToArray();
    }

    public async Task Refresh(CancellationToken cancellationToken)
    {
        using var response = await this.http.PostAsync("api/crypto/sync", null, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }
    }
}
