using System.Text.Json;
using TradingCompany.Infrastructure;

// This separate process reads the owner's .env directly, never exports it to environment/child models.
// It only reads the wallet. It does not register an executor, arm, place or cancel orders.
var unattended = args.Length == 1 && args[0] == "--check-env";
if (!unattended && (Console.IsInputRedirected || Console.IsOutputRedirected || args.Length != 0))
    return 2;

if (!Console.IsOutputRedirected) Console.Title = "Quantara - Bitkub read-only connection";
Console.WriteLine("BITKUB READ-ONLY CONNECTION CHECK");
Console.WriteLine("Create a NEW key with Trade, Deposit and Withdraw unchecked.");
Console.WriteLine("Reads BITKUB_API_KEY and BITKUB_API_SECRET from the project's .env file.");
Console.WriteLine("Credentials are never printed or exported to environment variables.");
Console.WriteLine("This reads your real wallet. Live trading remains DISABLED.");

var exitCode = 1;
var reportPath = Path.Combine(AppContext.BaseDirectory, "wallet-read-result.json");
try
{
    // Replace any earlier success before attempting a new check, so it cannot appear current after failure.
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        observedAt = DateTimeOffset.UtcNow, walletReadSucceeded = false, liveEnabled = false
    }));
    var (key, secret) = BitkubCredentialsFile.Read(Path.GetFullPath(".env"));
    using var broker = new BitkubLiveBroker(key, secret);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    Console.WriteLine("Checking signed wallet read...");
    var wallet = await broker.GetWalletAsync(deadline.Token);
    var thb = wallet.Single(b => b.Currency == "THB");
    Console.WriteLine($"CONNECTED: THB available {thb.Available}, reserved {thb.Reserved}, total {thb.Total}");
    var nonzeroCoins = wallet.Where(b => b.Currency != "THB" && b.Total != 0).Select(b => b.Currency).ToArray();
    Console.WriteLine(nonzeroCoins.Length == 0 ? "No nonzero coin balances reported." :
        "Nonzero coin balances reported: " + string.Join(", ", nonzeroCoins));
    Console.WriteLine("Read access verified. Account identity, permissions and Live readiness are not verified by this check.");

    // Non-secret evidence only, beside this published executable, inside ignored .runtime.
    var report = new
    {
        observedAt = DateTimeOffset.UtcNow,
        source = "https://api.bitkub.com/api/v4/wallet/balances",
        walletReadSucceeded = true,
        liveEnabled = false,
        thbAvailable = thb.Available, thbReserved = thb.Reserved, thbTotal = thb.Total,
        nonzeroCoins
    };
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Non-secret result saved locally: " + reportPath);
    exitCode = 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Check cancelled or timed out. No order was sent.");
}
catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or UnauthorizedAccessException)
{
    Console.WriteLine("Could not read credentials. Fill BITKUB_API_KEY and BITKUB_API_SECRET in the project's .env.");
    Console.WriteLine("Use one entry for each, without duplicates. No order sent.");
}
catch (Exception)
{
    // Never print credentials, signatures, broker response bodies or provider exceptions.
    Console.WriteLine("Wallet read failed. Check the key/secret, Bitkub key status, IP restriction and network.");
    Console.WriteLine("No order sent; credentials remain only in your local .env.");
}
if (!unattended)
{
    Console.WriteLine("Press Enter to return.");
    Console.ReadLine();
}
return exitCode;
