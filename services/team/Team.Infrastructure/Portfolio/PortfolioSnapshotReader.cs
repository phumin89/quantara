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

    public PortfolioSnapshotReader(HttpClient http, IMarketResearchReader? marketResearchReader = null)
    {
        this.http = http;
        this.marketResearchReader = marketResearchReader;
    }

    public Task<string> Read(CancellationToken cancellationToken) => this.ReadForMarkets([], cancellationToken);

    public async Task<string> ReadForMarkets(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
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
                currentPrice = PortfolioSnapshotReader.OptionalDecimal(item, "currentPrice"),
                value = PortfolioSnapshotReader.OptionalDecimal(item, "value"),
                averageCost = PortfolioSnapshotReader.OptionalDecimal(item, "averageCost"),
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

        var marketResearch = await this.ReadMarketResearch(accountKey, symbols, cancellationToken);
        var openOrders = await this.ReadOpenOrders(cancellationToken);
        var tradingOverview = await this.ReadTradingMode(accountKey, cancellationToken);
        if (await new CurrentAccountReader(this.http).Read(cancellationToken) != accountKey)
        {
            throw new InvalidOperationException("Account changed while collecting order and market evidence.");
        }

        var json = JsonSerializer.Serialize(
            new
            {
                accountKey,
                tradingMode = TradingMode(tradingOverview),
                tradingPerformance = TradingPerformance(tradingOverview),
                walletEvidence = wallet.Evidence,
                positions,
                summary = new
                {
                    totalAssets = PortfolioSnapshotReader.OptionalDecimal(summary, "totalAssets"),
                    availableThb = wallet.AvailableThb,
                    holdingCount = PortfolioSnapshotReader.OptionalDecimal(summary, "holdingCount"),
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
        if (json.Length > 40000)
        {
            throw new InvalidOperationException("Portfolio snapshot is too large.");
        }

        return json;
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
        var age = DateTimeOffset.UtcNow - observedAt;
        if (age > TimeSpan.FromSeconds(60) || age < TimeSpan.FromSeconds(-5))
        {
            throw new InvalidOperationException("Wallet evidence is stale or from the future.");
        }

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relevantBalances = new List<JsonElement>();
        decimal? availableThb = null;
        foreach (var balance in root.GetProperty("balances").EnumerateArray())
        {
            var symbol = balance.GetProperty("symbol").GetString();
            var quantity = balance.GetProperty("quantity").GetDecimal();
            var available = PortfolioSnapshotReader.OptionalDecimal(balance, "available");
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
                await this.marketResearchReader.ReadForMarkets(accountKey, symbols, cancellationToken)
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
                observedAt = DateTimeOffset.UtcNow,
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

    private static string? TradingMode(JsonElement? overview) =>
        overview is { } value
        && value.TryGetProperty("mode", out var mode)
        && mode.ValueKind == JsonValueKind.String
        && mode.GetString() is "stopped" or "paper" or "manualReview" or "autoLive"
            ? mode.GetString()
            : null;

    private static Dictionary<string, object?> TradingPerformance(JsonElement? overview)
    {
        if (
            TradingMode(overview) is null
            || overview is not { } value
            || !value.TryGetProperty("autonomy", out var performance)
        )
        {
            return new() { ["available"] = false, ["reason"] = "unavailable" };
        }

        if (
            performance.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("heartbeatAt", out var heartbeat)
            || heartbeat.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(heartbeat.GetString(), out var heartbeatAt)
            || DateTimeOffset.UtcNow - heartbeatAt > TimeSpan.FromSeconds(120)
            || DateTimeOffset.UtcNow - heartbeatAt < TimeSpan.FromSeconds(-5)
        )
        {
            return new() { ["available"] = false, ["reason"] = "stale" };
        }

        var result = new Dictionary<string, object?>
        {
            ["available"] = true,
            ["mode"] = TradingMode(overview),
            ["heartbeatAt"] = heartbeatAt,
            ["fault"] = ReadText(value, "fault"),
            ["dailyProfitTargetThb"] = ReadBoundedDecimalString(performance, "dailyProfitTargetThb"),
            ["realizedNetTodayThb"] = ReadBoundedDecimalString(performance, "realizedNetTodayThb"),
            ["scanCoverage"] = new
            {
                totalMarkets = ReadInteger(performance, "totalMarkets"),
                eligibleMarkets = ReadInteger(performance, "eligibleMarkets"),
                checkedMarkets = ReadInteger(performance, "checkedMarkets"),
            },
            ["activity"] = ReadText(performance, "activity"),
            ["opportunityReview"] = ReadOpportunityReview(performance),
            ["candidateReasons"] =
                performance.TryGetProperty("candidates", out var reasons) && reasons.ValueKind == JsonValueKind.Array
                    ? reasons
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.Object)
                        .Select(item => ReadText(item, "reason"))
                        .OfType<string>()
                        .Take(8)
                        .ToArray()
                    : Array.Empty<string>(),
        };
        return result;
    }

    private static string? ReadText(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() is { Length: <= 512 } text
                ? text
                : null
            : null;

    private static Dictionary<string, object?>? ReadOpportunityReview(JsonElement performance)
    {
        if (
            !performance.TryGetProperty("opportunityReview", out var review)
            || review.ValueKind != JsonValueKind.Object
            || ReadText(review, "protocol") != "observed-book-v1"
            || !review.TryGetProperty("days", out var days)
            || days.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        return new()
        {
            ["protocol"] = "observed-book-v1",
            ["assumptions"] = ReadText(review, "assumptions"),
            ["days"] = days.EnumerateArray()
                .Where(day => day.ValueKind == JsonValueKind.Object)
                .Take(7)
                .Select(day => new
                {
                    date = ReadText(day, "date"),
                    captured = ReadInteger(day, "captured"),
                    closed = ReadInteger(day, "closed"),
                    waiting = ReadInteger(day, "waiting"),
                    unknown = ReadInteger(day, "unknown"),
                    meanNetReturnFraction = day.TryGetProperty("meanNetReturnFraction", out var fraction)
                    && fraction.ValueKind == JsonValueKind.Number
                    && fraction.TryGetDecimal(out var value)
                    && value is >= -1 and <= 1000
                        ? value
                        : (decimal?)null,
                })
                .ToArray(),
        };
    }

    private static decimal? ReadBoundedDecimalString(JsonElement value, string property)
    {
        if (
            !value.TryGetProperty(property, out var item)
            || item.ValueKind != JsonValueKind.String
            || !decimal.TryParse(
                item.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number
            )
        )
        {
            return null;
        }

        return number is >= -1_000_000_000_000m and <= 1_000_000_000_000m ? number : null;
    }

    private static int? ReadInteger(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.Number
        && item.TryGetInt32(out var number)
        && number is >= 0 and <= 1000
            ? number
            : null;

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
                    .Select(PortfolioSnapshotReader.ToTradeEvidence)
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

    private static object ToTradeEvidence(JsonElement item)
    {
        return new
        {
            transactionId = item.GetProperty("transactionId").GetString(),
            symbol = item.GetProperty("symbol").GetString(),
            side = item.GetProperty("side").GetString(),
            quantity = item.GetProperty("quantity").GetDecimal(),
            price = item.GetProperty("price").GetDecimal(),
            fee = item.GetProperty("fee").GetDecimal(),
            feeCredit = item.GetProperty("feeCredit").GetDecimal(),
            timestamp = item.GetProperty("timestamp").Clone(),
        };
    }

    public async Task Refresh(CancellationToken cancellationToken)
    {
        using var response = await this.http.PostAsync("api/crypto/sync", null, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static decimal? OptionalDecimal(JsonElement value, string property)
    {
        var number = value.GetProperty(property);

        return number.ValueKind == JsonValueKind.Null ? null : number.GetDecimal();
    }
}
