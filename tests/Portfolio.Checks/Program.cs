using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Portfolio.Checks;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

if (args.Contains("--telegram-reviews-only", StringComparer.Ordinal))
{
    await TelegramReviewChecks.Run();
    return;
}

if (args.Contains("--submission-evidence-only", StringComparer.Ordinal))
{
    await TradingSubmissionEvidenceChecks.Run();
    return;
}

if (args.Contains("--policy-only", StringComparer.Ordinal))
{
    await TradingPolicyChecks.Run();
    return;
}

if (args.Contains("--sql-fixture-configuration-only", StringComparer.Ordinal))
{
    CryptoSqlTestDatabaseChecks.Run();
    return;
}

if (args.Contains("--manual-sale-allocation-sql-only", StringComparer.Ordinal))
{
    await ManualSaleAllocationSqlChecks.Run();
    return;
}

if (args.Contains("--manual-sale-reconciliation-only", StringComparer.Ordinal))
{
    await ManualSaleReconciliationChecks.Run();
    return;
}

if (args.Contains("--pending-exits-only", StringComparer.Ordinal))
{
    await TradingPendingExitChecks.Run();
    return;
}

if (args.Contains("--company-simulation-only", StringComparer.Ordinal))
{
    await CompanySimulationChecks.Run();
    return;
}

if (args.Contains("--company-importer-only", StringComparer.Ordinal))
{
    await CompanyCapitalImporterChecks.Run();
    return;
}

if (args.Contains("--company-import-only", StringComparer.Ordinal))
{
    CompanyCapitalChecks.Run();
    await CompanyCapitalImportChecks.Run();
    return;
}

if (args.Contains("--company-capital-only", StringComparer.Ordinal))
{
    CompanyCapitalChecks.Run();
    await PortfolioGoalChecks.Run();
    await RiskEvidenceChecks.Run();
    await RiskPipelineChecks.Run();
    return;
}

if (args.Contains("--storage-only", StringComparer.Ordinal))
{
    await TradingStorageChecks.Run();
    return;
}

if (args.Contains("--storage-sql-only", StringComparer.Ordinal))
{
    await TradingPersistenceSqlChecks.Run();
    return;
}

if (args.Contains("--reconciliation-isolation-only", StringComparer.Ordinal))
{
    await TradingIsolationChecks.Run();
    return;
}

if (args.Contains("--paper-provenance-only", StringComparer.Ordinal))
{
    PaperCapitalProvenanceChecks.Run();
    return;
}

if (args.Contains("--telegram-receipts-sql-only", StringComparer.Ordinal))
{
    await TradingNotificationReceiptSqlChecks.Run(
        legacy: args.Contains("--legacy-ci-receipts", StringComparer.Ordinal)
    );
    return;
}

if (args.Contains("--portfolio-accounting-only", StringComparer.Ordinal))
{
    await PortfolioAccountingChecks.Run();
    return;
}

if (args.Contains("--telegram-recovery-only", StringComparer.Ordinal))
{
    await TradingDispatchMigrationSqlChecks.Run();
    await TradingNotificationRecoveryChecks.Run();
    await TradingDeliveryChecks.Run();
    await TradingReportCompletionChecks.Run();
    await HangfireDeliveryChecks.Run();
    return;
}

if (args.Contains("--dispatch-migration-sql-only", StringComparer.Ordinal))
{
    await TradingDispatchMigrationSqlChecks.Run();
    return;
}

if (args.Contains("--feature-sql-only", StringComparer.Ordinal))
{
    await PortfolioGoalSqlChecks.Run();
    Console.WriteLine("Portfolio goal SQL checks passed.");
    return;
}

if (args.Contains("--hangfire-sql-only", StringComparer.Ordinal))
{
    await HangfireSqlChecks.Run();
    return;
}

await OrderChecks.Run();
await PortfolioGoalChecks.Run();
CompanyCapitalChecks.Run();
await CompanyCapitalImportChecks.Run();
await CompanyCapitalImporterChecks.Run();
await CompanySimulationChecks.Run();
await PortfolioGoalSqlChecks.Run();
await AdaptiveRiskChecks.Run();
await AutonomousTradingChecks.Run();
await AutonomousRiskCacheChecks.Run();
await TradingMarketEvidenceChecks.Run();
await TradingMarketTransportChecks.Run();
await TradingSubmissionEvidenceChecks.Run();
await TradingReconciliationStopChecks.Run();
await AutonomousAuthorityChecks.Run();
await TradingShadowChecks.Run();
await TradingWatchChecks.Run();
await PortfolioLastBuyChecks.Run();
await PortfolioAccountingChecks.Run();
await AutonomousCompatibilityChecks.Run();
await TradingFillNoticeChecks.Run();
await AdaptiveTradingStrategyChecks.Run();
await AdaptiveExitChecks.Run();
await TradingExitStudyChecks.Run();
await TradingTimeframeChecks.Run();
await TradingReplayChecks.Run();
await TradingPolicyChecks.Run();
await TradingHistoryReaderChecks.Run();
await TradingEventQueueChecks.Run();
await TradingEventSourceChecks.Run();
await RealtimeTradingChecks.Run();
await RealtimeWorkerChecks.Run();
await RiskEvidenceChecks.Run();
await RiskPipelineChecks.Run();
await RiskFillChecks.Run();
await OrderRecoveryChecks.Run();
await OrderDiscoveryChecks.Run();
await OrderSourceChecks.Run();
await AccountEvidenceChecks.Run();
await TelegramReviewChecks.Run();
await TradingDomainChecks.Run();
await TradingAccountingChecks.Run();
await ManualSaleReconciliationChecks.Run();
await TradingStorageChecks.Run();
await TradingReportChecks.Run();
await TradingDeliveryChecks.Run();
await TradingReportCompletionChecks.Run();
await OrderMonitorChecks.Run();
await TradingNotificationRecoveryChecks.Run();
await HangfireDeliveryChecks.Run();
await HangfireSqlChecks.Run();
await TradingDispatchMigrationSqlChecks.Run();
await TradingProjectionChecks.Run();
await TradingEngineChecks.Run();
await TradingPendingExitChecks.Run();
await TradingIsolationChecks.Run();
PaperCapitalProvenanceChecks.Run();
await TradingPrecisionChecks.Run();
await TradingActivationPrecisionChecks.Run();
await TradingControlChecks.Run();
await TradingPreparationChecks.Run();
await TradingBrokerChecks.Run();
await BrokerNumberChecks.Run();
await TelegramMessageChecks.Run();

static void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(name);
    }

    Console.WriteLine("Passed: " + name);
}

static bool RejectsJson(Action operation)
{
    try
    {
        operation();
        return false;
    }
    catch (JsonException)
    {
        return true;
    }
}

using var buy = JsonDocument.Parse(
    """{"txn_id":"trade-1","side":"buy","rate":"100","amount":"500","fee":"1.25","credit":"0.5","ts":1700000000000} """
);
var buyTrade = BitkubAccountReader.ParseTrade(buy.RootElement, "TEST");
Check(
    buyTrade.Quantity == 5 && buyTrade.Fee == 1.25m && buyTrade.FeeCredit == 0.5m,
    "buy uses quote amount and preserves fee/credit"
);
using var sell = JsonDocument.Parse(
    """{"txn_id":"trade-2","side":"sell","rate":"120","amount":"3","fee":"0.9","credit":"0","ts":1700000100000} """
);
var sellTrade = BitkubAccountReader.ParseTrade(sell.RootElement, "TEST");
Check(sellTrade.Quantity == 3, "sell uses base quantity");
Check(
    buyTrade.Side == TradeSide.Buy && sellTrade.Side == TradeSide.Sell,
    "external trade sides map to typed domain values"
);
var storageOptions = PortfolioJson.CreateOptions();
const string legacyTradeJson =
    """{"TransactionId":"legacy-1","Symbol":"TEST","Side":"buy","Quantity":5,"Price":100,"Fee":1.25,"FeeCredit":0.5,"Timestamp":"2023-11-14T22:13:20+00:00"}""";
var legacyTrade =
    JsonSerializer.Deserialize<Trade>(legacyTradeJson, storageOptions)
    ?? throw new InvalidOperationException("Legacy trade was not loaded.");
Check(
    legacyTrade.Side == TradeSide.Buy && legacyTrade.Quantity == 5 && legacyTrade.Fee == 1.25m,
    "legacy SQL trade JSON retains side, quantity and fees"
);
using var storedTrade = JsonDocument.Parse(JsonSerializer.Serialize(legacyTrade, storageOptions));
Check(
    storedTrade.RootElement.GetProperty("Side").GetString() == "buy"
        && storedTrade.RootElement.GetProperty("TransactionId").GetString() == "legacy-1",
    "SQL JSON preserves Pascal property names and lowercase trade side"
);
var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
PortfolioJson.ConfigureOptions(webOptions);
using var apiTrade = JsonDocument.Parse(JsonSerializer.Serialize(sellTrade, webOptions));
Check(
    apiTrade.RootElement.GetProperty("side").GetString() == "sell",
    "API trade JSON preserves camelCase properties and lowercase side"
);
foreach (var invalidSide in new[] { "0", "null", "\"hold\"", "\"BUY\"" })
{
    Check(
        RejectsJson(() => JsonSerializer.Deserialize<TradeSide>(invalidSide, storageOptions)),
        "invalid enum JSON fails closed: " + invalidSide
    );
}

Check(
    RejectsJson(() => JsonSerializer.Serialize((TradeSide)999, storageOptions)),
    "undefined trade side cannot be serialized"
);
Check(
    RejectsJson(() => JsonSerializer.Serialize((PositionStatus)999, storageOptions)),
    "undefined position status cannot be serialized"
);
Check(
    RejectsJson(() => JsonSerializer.Serialize((CostStatus)999, storageOptions)),
    "undefined cost status cannot be serialized"
);
Check(
    RejectsJson(() =>
        JsonSerializer.Deserialize<Trade>(
            legacyTradeJson.Replace("\"Side\":\"buy\",", "", StringComparison.Ordinal),
            storageOptions
        )
    ),
    "missing legacy trade side cannot default to Buy"
);
var ledger = new PortfolioLedger { Balances = [new("TEST", 2)], Prices = [new("TEST", 120)] };
ledger = ledger.Remember(new("TEST", "Test coin"), [buyTrade, sellTrade, buyTrade]);
Check(ledger.Trades.Length == 2 && ledger.Coins.Length == 1, "reimport deduplicates transaction and coin");
var holding = PortfolioProjection.Positions(ledger).Single();
using var apiPosition = JsonDocument.Parse(JsonSerializer.Serialize(holding, webOptions));
Check(
    apiPosition.RootElement.GetProperty("status").GetString() == "Holding"
        && apiPosition.RootElement.GetProperty("costStatus").GetString() == "Unknown",
    "API position enum spellings remain compatible"
);
var oldPayload =
    "{\"Trades\":["
    + legacyTradeJson
    + "],\"Coins\":[{\"Symbol\":\"TEST\",\"Name\":\"Test coin\"}],\"Balances\":[{\"Symbol\":\"THB\",\"Quantity\":1000}]}";
var oldLedger =
    JsonSerializer.Deserialize<PortfolioLedger>(oldPayload, storageOptions)
    ?? throw new InvalidOperationException("Legacy ledger was not loaded.");
Check(
    oldLedger.Trades.Single().Side == TradeSide.Buy && oldLedger.Balances.Single().Available is null,
    "legacy SQL ledger remains readable without invented available cash"
);
var ledgerRoundtrip =
    JsonSerializer.Deserialize<PortfolioLedger>(JsonSerializer.Serialize(oldLedger, storageOptions), storageOptions)
    ?? throw new InvalidOperationException("Ledger roundtrip failed.");
Check(
    ledgerRoundtrip.Trades.SequenceEqual(oldLedger.Trades) && ledgerRoundtrip.Coins.SequenceEqual(oldLedger.Coins),
    "enum migration preserves persisted fills and retained coins"
);
Check(
    holding.Quantity == 2 && holding.Value == 240 && holding.Status == PositionStatus.Holding,
    "reconciled balance drives holdings"
);
Check(
    holding.AverageCost is null && holding.RealizedPnl is null && holding.UnrealizedPnl is null,
    "partial history never invents cost or profit"
);
var closedLedger = ledger with { Balances = [], Prices = [] };
var closed = PortfolioProjection.Positions(closedLedger).Single();
Check(
    closed.Status == PositionStatus.Closed && closed.Value == 0 && closed.Quantity == 0,
    "sold coin remains visible without a market price"
);
Check(
    PortfolioProjection.Positions(ledger with { Prices = [] }).Single().Value is null,
    "missing price never becomes zero holding value"
);

var exchangeHandler = new FakeReadHandler();
using var http = new HttpClient(exchangeHandler) { BaseAddress = new Uri("https://api.bitkub.com") };
var reader = new BitkubAccountReader(http, new("test-key", "test-secret"));
var wallet = await reader.Balances(CancellationToken.None);
Check(
    wallet.Single(balance => balance.Symbol == "THB") is { Quantity: 1000, Available: 700 },
    "wallet retains available THB separately from reserved cash"
);
var summaryLedger = ledger with { UpdatedAt = DateTimeOffset.UnixEpoch, Balances = wallet };
var summary = PortfolioProjection.Summary(summaryLedger);
Check(
    summary.TotalAssets == 240 && summary.AvailableThb == 700 && summary.HoldingCount == 1,
    "summary excludes THB from crypto value and coin count"
);
Check(
    PortfolioProjection.Summary(summaryLedger with { Coins = [] }).TotalAssets == 240,
    "summary includes unremembered positive wallet balances"
);
Check(
    PortfolioProjection.Summary(summaryLedger with { Prices = [] }).TotalAssets is null,
    "summary never reports a partial valuation as a total"
);
Check(
    PortfolioProjection.Summary(new PortfolioLedger()) is { TotalAssets: null, AvailableThb: null, HoldingCount: null },
    "initial summary remains unknown before wallet sync"
);
using var marketHttp = new HttpClient(new Quantara.Portfolio.Checks.FakeMarketDataHandler())
{
    BaseAddress = new Uri("http://market-data"),
};
var marketData = new MarketDataClient(marketHttp);
Check(
    (await marketData.GetCoins(CancellationToken.None)).Single() is { Symbol: "BTC", Name: "Bitcoin" },
    "market data validates coins contract"
);
Check(
    (await marketData.GetQuotes(CancellationToken.None)).Single() is { Symbol: "BTC", Price: 100.5m },
    "market data validates quotes contract"
);
var failedMarket = new Quantara.Portfolio.Checks.FakeMarketDataHandler { Fail = true };
var failedMarketClient = new MarketDataClient(
    new HttpClient(failedMarket) { BaseAddress = new Uri("http://market-data") }
);
var marketFailedClosed = false;
try
{
    await failedMarketClient.GetCoins(CancellationToken.None);
}
catch (HttpRequestException)
{
    marketFailedClosed = true;
}

Check(marketFailedClosed, "market data HTTP failure fails closed");
using var incompleteMarketHttp = new HttpClient(
    new Quantara.Portfolio.Checks.FakeMarketDataHandler { MissingSource = true }
)
{
    BaseAddress = new Uri("http://market-data"),
};
var incompleteMarketClient = new MarketDataClient(incompleteMarketHttp);
var rejectedContract = false;
try
{
    await incompleteMarketClient.GetCoins(CancellationToken.None);
}
catch (JsonException)
{
    rejectedContract = true;
}

Check(rejectedContract, "market data never invents exchange source for a missing field");
Check(
    PortfolioProjection.Summary(summaryLedger with { Balances = [new("THB", 1000, 700)] })
        is { TotalAssets: 0, HoldingCount: 0 },
    "cash-only wallet has no held crypto value"
);
var legacyBalance =
    JsonSerializer.Deserialize<Balance>("""{"Symbol":"THB","Quantity":1000}""")
    ?? throw new InvalidOperationException("Legacy balance was not deserialized.");
Check(
    legacyBalance.Available is null
        && PortfolioProjection.Summary(summaryLedger with { Balances = [legacyBalance] }).AvailableThb is null,
    "legacy payload never substitutes total cash for available cash"
);
exchangeHandler.InvalidWallet = true;
var rejectedWallet = false;
try
{
    await reader.Balances(CancellationToken.None);
}
catch (InvalidDataException)
{
    rejectedWallet = true;
}

Check(rejectedWallet, "wallet rejects available cash exceeding the total");
var history = await reader.History("TEST", CancellationToken.None);
Check(history.Length == 2 && exchangeHandler.HistoryCalls == 2, "history follows keyset pagination");
Check(exchangeHandler.VerifiedSignatures == 2, "signed GET uses exact timestamp, path and query");

if (args.Contains("--sql"))
{
    var account = Convert
        .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Quantara deterministic checks account")))
        .ToLowerInvariant();
    var connection = await CryptoSqlTestDatabase.Connection("QUANTARA_PORTFOLIO_TEST_SQL");
    var first = new SqlPortfolioRepository(connection, account);
    await first.Initialize(false, CancellationToken.None);
    await first.Save(closedLedger, CancellationToken.None);
    var second = new SqlPortfolioRepository(connection, account);
    var restored = await second.Load(CancellationToken.None);
    Check(
        restored.Coins.Length == 1
            && restored.Trades.Length == 2
            && PortfolioProjection.Positions(restored).Single().Status == PositionStatus.Closed,
        "SQL Server retains closed coin and fills across store instances"
    );
}
