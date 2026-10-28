using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;
using Quantara.Portfolio.Infrastructure.Risk;

namespace Portfolio.Checks;

public static class BrokerNumberChecks
{
    private static readonly DateTimeOffset recordedAt = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

    public static async Task Run()
    {
        ScalarEvidence();
        ManualSaleEvidence();
        await ManualFillEvidence();
        await AutoFillEvidence();
        await AccountEvidence();
        await MarketEvidence();
        Console.WriteLine(
            "Broker number checks passed: exact source decimals, signed/zero compatibility and six public readers; synthetic GETs only."
        );
    }

    private static void ScalarEvidence()
    {
        foreach (
            var (json, expected) in new[]
            {
                ("0", 0m),
                ("\"0.000\"", 0m),
                ("-0.0", 0m),
                ("0e-29", 0m),
                ("\"+0e29\"", 0m),
                ("1e-28", 0.0000000000000000000000000001m),
                ("-1e-28", -0.0000000000000000000000000001m),
                ("\" +001.2300e+1 \"", 12.3m),
                ("\" -001.2300e+1 \"", -12.3m),
                ("\".5000\"", .5m),
                ("12345678901.234567890123456789", 12345678901.234567890123456789m),
                ("79228162514264337593543950335", decimal.MaxValue),
                ("-79228162514264337593543950335", decimal.MinValue),
                (JsonSerializer.Serialize("1." + new string('0', 100)), 1m),
            }
        )
        {
            var trade = BitkubAccountReader.ParseTrade(Set(Fill(), "fee", Scalar(json)), "BTC");
            Check(trade.Fee == expected, "Exact Float scalar changed value: " + json);
        }

        foreach (
            var json in new[]
            {
                "1.00000000000000000000000000001",
                "\"1.00000000000000000000000000001\"",
                "12345678901234567890.1234567891",
                "0.00000000000000000000000000001",
                "1e-29",
                "\"1e-29\"",
                JsonSerializer.Serialize("1\0"),
                JsonSerializer.Serialize("0\0"),
                JsonSerializer.Serialize("1e0\0"),
                "null",
                "true",
                "false",
                "[1]",
                "{\"value\":1}",
                "\"\"",
                "\" \"",
                "1e1000",
            }
        )
        {
            Reject(
                () => BitkubAccountReader.ParseTrade(Set(Fill(), "fee", Scalar(json)), "BTC"),
                "Inexact or malformed source scalar accepted: " + json
            );
        }

        Check(
            BitkubAccountReader.ParseTrade(Set(Fill(), "amount", Scalar("0")), "BTC").Quantity == 0m,
            "Legacy zero-quantity imports must remain valid."
        );
    }

    private static void ManualSaleEvidence()
    {
        var trade = BitkubAccountReader.ParseTrade(Fill(), "BTC");
        var baseline = Info(Fill(), "order-1", "", 1m, 1m);
        Check(
            BitkubManualSaleEvidenceReader
                .VerifyOrder(baseline, trade, "order-1")
                .StartsWith("bitkub-sale-v1:", StringComparison.Ordinal),
            "Exact manual-sale evidence must still produce its existing proof family."
        );
        foreach (var field in new[] { "remaining", "filled", "total", "amount", "rate", "fee", "credit" })
        {
            var original = field switch
            {
                "remaining" or "fee" or "credit" => "0",
                "rate" => "100",
                _ => "1",
            };
            foreach (var value in WireForms(Rounded(original)))
            {
                var order = field is "remaining" or "filled" or "total"
                    ? Set(baseline, field, value)
                    : Info(Set(Fill(), field, value), "order-1", "", 1m, 1m);
                Reject(
                    () => BitkubManualSaleEvidenceReader.VerifyOrder(order, trade, "order-1"),
                    "Manual-sale proof accepted rounded " + field
                );
            }
        }
    }

    private static async Task ManualFillEvidence()
    {
        var proposal = new OrderProposal(
            Guid.NewGuid(),
            "BTC",
            TradeSide.Buy,
            100m,
            100m,
            100m,
            "Numeric fixture",
            recordedAt.AddMinutes(-1),
            recordedAt.AddMinutes(1),
            OrderState.Submitted,
            "order-1",
            null
        );
        using var handler = new InspectionHttpHandler
        {
            ClientId = proposal.Id.ToString("N"),
            Status = "unfilled",
            Partial = true,
            OrderFilledJson = "1",
            OrderRemainingJson = "99",
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
        var inspector = new BitkubOrderInspector(
            http,
            new("fake-key", "fake-secret"),
            new OrderCheckTime { Now = recordedAt }
        );
        handler.HistoryJson = JsonSerializer.Serialize(new[] { Fill() });
        Check(
            (await inspector.Inspect(proposal, CancellationToken.None)).Fills?.Single().FeeThb == 0m,
            "Exact zero manual fill fees must remain valid."
        );
        foreach (var field in new[] { "amount", "rate", "fee", "credit", "filled", "remaining" })
        {
            var original = field switch
            {
                "amount" or "filled" => "1",
                "rate" => "100",
                "remaining" => "99",
                _ => "0",
            };
            foreach (var value in WireForms(Rounded(original)))
            {
                handler.OrderFilledJson = field == "filled" ? value.GetRawText() : "1";
                handler.OrderRemainingJson = field == "remaining" ? value.GetRawText() : "99";
                handler.HistoryJson = JsonSerializer.Serialize(
                    new[] { field is "filled" or "remaining" ? Fill() : Set(Fill(), field, value) }
                );
                await RejectAsync(
                    () => inspector.Inspect(proposal, CancellationToken.None),
                    "Manual fill accepted rounded " + field
                );
            }
        }

        Check(handler.Posts == 0, "Manual numeric inspection cannot submit orders.");
    }

    private static async Task AutoFillEvidence()
    {
        var now = DateTimeOffset.UtcNow;
        var intent = new TradingIntent(
            Guid.NewGuid(),
            "BTC",
            TradeSide.Buy,
            100m,
            100m,
            100m,
            now.AddMinutes(-1),
            0,
            "fake-hash",
            "Numeric fixture",
            "submitted",
            "fixture-order"
        );
        var row = Fill(99m, 1m, .5m, now);
        var baseline = Info(row, "fixture-order", intent.Id.ToString("N"), 100m, 99m);
        using var handler = new BrokerNumberHttpHandler(_ =>
            JsonSerializer.Serialize(new { error = 0, result = baseline })
        );
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://bitkub.invalid/") };
        var exchange = new BitkubTradingExchange(http, new("fake-key", "fake-secret"), new FakeOrderBroker());
        Check(
            (await exchange.Inspect(intent, CancellationToken.None)).Fills.Single().FeeThb == .5m,
            "Exact AutoLive fill values must remain valid."
        );
        foreach (var field in new[] { "amount", "rate", "fee", "credit", "remaining", "filled" })
        {
            var original = field switch
            {
                "amount" or "filled" => "99",
                "rate" => "100",
                "fee" => "1",
                "credit" => "0.5",
                _ => "0",
            };
            foreach (var value in WireForms(Rounded(original)))
            {
                var order = field is "remaining" or "filled"
                    ? Set(baseline, field, value)
                    : Info(Set(row, field, value), "fixture-order", intent.Id.ToString("N"), 100m, 99m);
                using var invalidHandler = new BrokerNumberHttpHandler(_ =>
                    JsonSerializer.Serialize(new { error = 0, result = order })
                );
                using var invalidHttp = new HttpClient(invalidHandler) { BaseAddress = http.BaseAddress };
                var invalidExchange = new BitkubTradingExchange(
                    invalidHttp,
                    new("fake-key", "fake-secret"),
                    new FakeOrderBroker()
                );
                await RejectAsync(
                    () => invalidExchange.Inspect(intent, CancellationToken.None),
                    "Auto fill accepted rounded " + field
                );
            }
        }

        foreach (var text in new[] { "+1", "1e0", " 1 ", "1\0" })
        {
            var order = Info(
                Set(row, "fee", JsonSerializer.SerializeToElement(text)),
                "fixture-order",
                intent.Id.ToString("N"),
                100m,
                99m
            );
            using var invalidHandler = new BrokerNumberHttpHandler(_ =>
                JsonSerializer.Serialize(new { error = 0, result = order })
            );
            using var invalidHttp = new HttpClient(invalidHandler) { BaseAddress = http.BaseAddress };
            var invalidExchange = new BitkubTradingExchange(
                invalidHttp,
                new("fake-key", "fake-secret"),
                new FakeOrderBroker()
            );
            await RejectAsync(
                () => invalidExchange.Inspect(intent, CancellationToken.None),
                "Auto decimal-point grammar expanded."
            );
        }
    }

    private static async Task AccountEvidence()
    {
        using (var baselineHandler = new BrokerNumberHttpHandler(path => AccountResponse(path, "", default)))
        using (var baselineHttp = new HttpClient(baselineHandler) { BaseAddress = new Uri("https://bitkub.invalid/") })
        {
            var baselineReader = new BitkubRiskEvidenceReader(
                new BitkubAccountReader(baselineHttp, new("fake-key", "fake-secret")),
                new OrderCheckTime { Now = recordedAt }
            );
            var observed = await baselineReader.Read(CancellationToken.None);
            Check(
                observed.Balances.Single().Available == 4000m && observed.CashFlows.Single().ValueThb == 190m,
                "Exact account/flow controls must remain valid before rejecting inexact variants."
            );
        }

        foreach (var field in new[] { "total", "available", "open-amount", "open-rate", "cash-amount", "cash-fee" })
        {
            var original = field switch
            {
                "total" => "5000",
                "available" => "4000",
                "cash-amount" => "200",
                "cash-fee" => "10",
                _ => "100",
            };
            foreach (var value in WireForms(Rounded(original)))
            {
                using var handler = new BrokerNumberHttpHandler(path => AccountResponse(path, field, value));
                using var http = new HttpClient(handler) { BaseAddress = new Uri("https://bitkub.invalid/") };
                var reader = new BitkubRiskEvidenceReader(
                    new BitkubAccountReader(http, new("fake-key", "fake-secret")),
                    new OrderCheckTime { Now = recordedAt }
                );
                await RejectAsync(
                    () => reader.Read(CancellationToken.None),
                    "Account evidence accepted rounded " + field
                );
            }
        }
    }

    private static async Task MarketEvidence()
    {
        using (var baselineHandler = new BrokerNumberHttpHandler(path => MarketResponse(path, "", default)))
        using (var baselineHttp = new HttpClient(baselineHandler) { BaseAddress = new Uri("https://bitkub.invalid/") })
        {
            var credentials = new BitkubCredentials("fake-key", "fake-secret");
            var baselineBroker = new BitkubOrderBroker(
                baselineHttp,
                credentials,
                new BitkubAccountReader(baselineHttp, credentials)
            );
            var market = await baselineBroker.Market("BTC", TradeSide.Buy, CancellationToken.None);
            Check(
                market.ReferencePrice == 100m && market.MinimumNotional == 10m && market.PriceStep == .01m,
                "Exact market controls must remain valid before rejecting inexact variants."
            );
        }

        foreach (var field in new[] { "last", "min_quote_size", "price_step" })
        {
            var original = field switch
            {
                "last" => "100",
                "min_quote_size" => "10",
                _ => "0.01",
            };
            foreach (var value in WireForms(Rounded(original)))
            {
                using var handler = new BrokerNumberHttpHandler(path => MarketResponse(path, field, value));
                using var http = new HttpClient(handler) { BaseAddress = new Uri("https://bitkub.invalid/") };
                var credentials = new BitkubCredentials("fake-key", "fake-secret");
                var broker = new BitkubOrderBroker(http, credentials, new BitkubAccountReader(http, credentials));
                await RejectAsync(
                    () => broker.Market("BTC", TradeSide.Buy, CancellationToken.None),
                    "Market accepted rounded " + field
                );
            }
        }
    }

    private static string AccountResponse(string path, string field, JsonElement value)
    {
        if (path == "/api/v4/wallet/balances")
        {
            var balance = JsonSerializer.SerializeToElement(
                new
                {
                    currency = "THB",
                    total = 5000m,
                    available = 4000m,
                }
            );
            if (field is "total" or "available")
            {
                balance = Set(balance, field, value);
            }

            return JsonSerializer.Serialize(new { code = 0, data = new[] { balance } });
        }

        if (path.StartsWith("/api/v3/market/my-open-orders", StringComparison.Ordinal))
        {
            var order = JsonSerializer.SerializeToElement(
                new
                {
                    id = "numeric-order",
                    side = "buy",
                    amount = 100m,
                    rate = 100m,
                }
            );
            if (field is "open-amount" or "open-rate")
            {
                order = Set(order, field[5..], value);
            }

            return JsonSerializer.Serialize(new { error = 0, result = new[] { order } });
        }

        if (path.StartsWith("/api/v4/fiat/withdraw/history", StringComparison.Ordinal))
        {
            var flow = JsonSerializer.SerializeToElement(
                new
                {
                    txn_id = "numeric-flow",
                    currency = "THB",
                    amount = 200m,
                    fee = 10m,
                    status = "complete",
                    time = recordedAt.ToUnixTimeSeconds(),
                }
            );
            if (field is "cash-amount" or "cash-fee")
            {
                flow = Set(flow, field[5..], value);
            }

            return JsonSerializer.Serialize(
                new
                {
                    code = 0,
                    data = new[] { flow },
                    pagination = new { page = 1 },
                }
            );
        }

        if (path.StartsWith("/api/v4/fiat/deposit/history", StringComparison.Ordinal))
        {
            return "{\"code\":0,\"data\":[],\"pagination\":{\"page\":1}}";
        }

        if (path.StartsWith("/api/v4/crypto/", StringComparison.Ordinal))
        {
            return "{\"code\":0,\"data\":{\"page\":1,\"total_page\":0,\"items\":[]}}";
        }

        return MarketResponse(path, "", default);
    }

    private static string MarketResponse(string path, string field, JsonElement value)
    {
        if (path == "/api/v3/market/symbols")
        {
            var symbol = JsonSerializer.SerializeToElement(
                new
                {
                    symbol = "BTC_THB",
                    status = "active",
                    market_segment = "SPOT",
                    source = "exchange",
                    freeze_buy = false,
                    freeze_sell = false,
                    price_scale = 2,
                    quote_asset_scale = 2,
                    base_asset_scale = 8,
                    min_quote_size = 10m,
                    price_step = .01m,
                }
            );
            if (field is "min_quote_size" or "price_step")
            {
                symbol = Set(symbol, field, value);
            }

            return JsonSerializer.Serialize(new { error = 0, result = new[] { symbol } });
        }

        if (path.StartsWith("/api/v3/market/ticker", StringComparison.Ordinal))
        {
            var quote = JsonSerializer.SerializeToElement(new { symbol = "BTC_THB", last = 100m });
            if (field == "last")
            {
                quote = Set(quote, field, value);
            }

            return JsonSerializer.Serialize(new[] { quote });
        }

        throw new InvalidOperationException("Unconfigured synthetic numeric endpoint: " + path);
    }

    private static JsonElement Fill(
        decimal amount = 1m,
        decimal fee = 0m,
        decimal credit = 0m,
        DateTimeOffset? at = null
    ) =>
        JsonSerializer.SerializeToElement(
            new
            {
                txn_id = "numeric-fill",
                side = "sell",
                amount,
                rate = 100m,
                fee,
                credit,
                timestamp = (at ?? recordedAt).ToUnixTimeMilliseconds(),
                ts = (at ?? recordedAt).ToUnixTimeMilliseconds(),
            }
        );

    private static JsonElement Info(JsonElement row, string id, string client, decimal amount, decimal filled) =>
        JsonSerializer.SerializeToElement(
            new
            {
                id,
                client_id = client,
                amount,
                rate = 100m,
                status = "filled",
                partial_filled = false,
                remaining = 0m,
                filled,
                total = filled,
                history = new[] { row },
            }
        );

    private static JsonElement Set(JsonElement record, string field, JsonElement value)
    {
        var fields = record
            .EnumerateObject()
            .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
        fields[field] = value;
        return JsonSerializer.SerializeToElement(fields);
    }

    private static JsonElement Scalar(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IEnumerable<JsonElement> WireForms(string value) =>
        [Scalar(value), JsonSerializer.SerializeToElement(value)];

    private static string Rounded(string original)
    {
        var point = original.IndexOf('.');
        return original
            + (point < 0 ? "." : "")
            + new string('0', 28 - (point < 0 ? 0 : original.Length - point - 1))
            + "1";
    }

    private static void Reject(Action operation, string message)
    {
        try
        {
            operation();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static async Task RejectAsync(Func<Task> operation, string message)
    {
        try
        {
            await operation();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
