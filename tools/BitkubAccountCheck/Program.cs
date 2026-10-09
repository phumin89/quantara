using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using TradingCompany.Infrastructure;

// Separate diagnostic process: direct file reads, no credential export, model or executor.
if (args.Length == 4 && args[0] == "--verify-evidence-env")
    return await BitkubProfileCommand.VerifyAsync(args[1..]);
if (args.Length == 2 && args[0] == "--verify-order-receipts-env")
    return await BitkubProfileCommand.VerifyAsync(args[1..]);
var captureEvidence = args.SequenceEqual(["--capture-evidence-env"]);
var preflight = captureEvidence || args.SequenceEqual(["--preflight-env"]);
var unattended = preflight || args.SequenceEqual(["--check-env"]);
if (!unattended && (Console.IsInputRedirected || Console.IsOutputRedirected || args.Length != 0)) return 2;
if (!Console.IsOutputRedirected) Console.Title = "Quantara - Bitkub connection check";
Console.WriteLine(preflight ? "BITKUB LIVE PREPARATION CHECK" : "BITKUB READ-ONLY CONNECTION CHECK");
Console.WriteLine("Uses the existing key/secret in the project's .env. Credentials are never printed or exported.");
Console.WriteLine("This does not enable Live, place/cancel orders or change the verified broker profile.");

var exitCode = 1;
var reportPath = Path.Combine(AppContext.BaseDirectory, captureEvidence
    ? $"broker-evidence-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}.json"
    : preflight ? "preflight-read-result.json" : "wallet-read-result.json");
var stage = "report";
try
{
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    { observedAt = DateTimeOffset.UtcNow, completed = false, walletReadSucceeded = false, liveEnabled = false }));
    stage = "credentials";
    var (key, secret) = BitkubCredentialsFile.Read(Path.GetFullPath(".env"));
    using var broker = new BitkubLiveBroker(key, secret);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(preflight ? 60 : 25));
    stage = "wallet";
    var wallet = await broker.GetWalletAsync(deadline.Token);
    var thb = wallet.Single(b => b.Currency == "THB");
    var nonzeroCoins = wallet.Where(b => b.Currency != "THB" && b.Total != 0).Select(b => b.Currency).ToArray();
    Console.WriteLine($"CONNECTED: THB available {thb.Available}, reserved {thb.Reserved}, total {thb.Total}");
    Console.WriteLine(nonzeroCoins.Length == 0 ? "No nonzero coin balances reported." : "Nonzero coin balances: " + string.Join(", ", nonzeroCoins));

    bool? tradePermissionConfirmed = null;
    int? openOrderCount = null;
    bool? stableWallet = null;
    var historyCounts = new Dictionary<string, int>();
    var marketRules = new List<BrokerSymbolRules>();
    var capturedHistory = new List<BrokerHistoryEntry>();
    var capturedOrders = new List<BrokerOrderEvidence>();
    if (preflight)
    {
        stage = "permission";
        try { tradePermissionConfirmed = await broker.CheckTradePermissionAsync(deadline.Token); }
        catch (InvalidDataException) { /* Unknown is not a denial or permission grant. */ }
        Console.WriteLine(tradePermissionConfirmed switch
        { true => "Trade permission probe succeeded. The issued token was discarded.", false => "Bitkub rejected the Trade permission probe (52).", _ => "Trade permission remains unknown: the token probe is unavailable." });
        stage = "orders-and-history";
        openOrderCount = 0;
        marketRules.AddRange(await broker.GetAvailableMarketsAsync(deadline.Token));
        var symbols = marketRules.Select(m => m.Symbol).ToArray();
        openOrderCount = (await broker.GetOpenOrdersForMarketsAsync(symbols, deadline.Token)).Count;
        var histories = await BitkubLiveBroker.ReadMarketBatchesAsync(symbols, async (symbol, token) =>
            (Symbol: symbol, History: await broker.GetHistoryAsync(symbol, token)), deadline.Token);
        foreach (var observation in histories)
        {
            var symbol = observation.Symbol;
            var history = observation.History;
            historyCounts[symbol] = history.Count;
            if (captureEvidence)
            {
                // Receipt-mode accounts can have history across many native markets.
                // Canary verification retains its separate 20-fill/two-order bound.
                // Every capture remains bounded and complete, never silently truncated.
                if (capturedHistory.Count + history.Count > 1000) throw new InvalidDataException("Capture history exceeds the evidence bound.");
                capturedHistory.AddRange(history);
                foreach (var identity in history.Select(h => (h.OrderId, h.Side)).Distinct())
                {
                    if (capturedOrders.Count == 1000) throw new InvalidDataException("Capture orders exceed the evidence bound.");
                    capturedOrders.Add(await broker.GetOrderAsync(symbol, identity.OrderId,
                        identity.Side == "buy" ? TradingCompany.Domain.OrderSide.Buy : TradingCompany.Domain.OrderSide.Sell, deadline.Token));
                }
            }
        }
        stage = "stable-wallet";
        var second = await broker.GetWalletAsync(deadline.Token);
        stableWallet = wallet.OrderBy(b => b.Currency, StringComparer.Ordinal).SequenceEqual(second.OrderBy(b => b.Currency, StringComparer.Ordinal));
        Console.WriteLine($"Open orders in the supported pairs: {openOrderCount}. Wallet stable during check: {stableWallet}.");
        Console.WriteLine($"Matched history records available: {historyCounts.Values.Sum()}. History alone does not verify settlement/rounding.");
    }
    stage = "report";
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        observedAt = DateTimeOffset.UtcNow, completed = true,
        source = "https://api.bitkub.com/api/v4/wallet/balances", walletReadSucceeded = true,
        liveEnabled = false, thbAvailable = thb.Available, thbReserved = thb.Reserved, thbTotal = thb.Total,
        nonzeroCoins, tradePermissionConfirmed, openOrderCount, historyCounts, stableWallet, marketRules,
        initialInventoryEmpty = preflight ? stableWallet == true && thb.Reserved == 0 && nonzeroCoins.Length == 0 && openOrderCount == 0 : (bool?)null,
        funded = thb.Total > 0,
        // Account/fill identifiers and balances stay private in ignored .runtime captures.
        // Typed evidence contains no API key, signature, secret or websocket token.
        credentialFingerprint = captureEvidence ? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "\0" + secret))) : null,
        walletBalances = captureEvidence ? wallet : null,
        orderHistory = captureEvidence ? capturedHistory : null,
        orderDetails = captureEvidence ? capturedOrders : null
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Diagnostic result saved locally. Owner activation and real execution remain unverified.");
    exitCode = 0;
}
catch (OperationCanceledException) { Console.WriteLine($"Check timed out at {stage}. No order was sent."); }
catch (Exception) when (stage == "credentials") { Console.WriteLine("Could not read credentials. Check the two unique key/secret entries in .env."); }
catch (Exception) { Console.WriteLine($"Check unavailable at {stage}. Check Bitkub key status, IP restriction and network. No order was sent."); }
if (!unattended) { Console.WriteLine("Press Enter to return."); Console.ReadLine(); }
return exitCode;
