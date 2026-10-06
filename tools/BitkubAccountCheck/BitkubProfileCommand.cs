using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingCompany.Infrastructure;

internal static class BitkubProfileCommand
{
    public static async Task<int> VerifyAsync(string[] captures)
    {
        try
        {
            var project = Directory.GetCurrentDirectory();
            var runtime = Path.GetFullPath(Path.Combine(project, ".runtime"));
            var settingsPath = Path.Combine(runtime, "owner-tls", "appsettings.Production.json");
            var settingsBytes = await File.ReadAllBytesAsync(settingsPath);
            using var settings = JsonDocument.Parse(settingsBytes);
            if (settings.RootElement.GetProperty("LiveExecution").GetProperty("Enabled").GetBoolean())
                throw new InvalidDataException();
            var evidence = new List<ReadOnlyMemory<byte>>();
            foreach (var capturePath in captures)
            {
                var path = Path.GetFullPath(capturePath);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!path.StartsWith(runtime + Path.DirectorySeparatorChar, comparison) ||
                    !Path.GetFileName(path).StartsWith("broker-evidence-", StringComparison.Ordinal) ||
                    new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException();
                evidence.Add(await File.ReadAllBytesAsync(path));
            }
            var (key, secret) = BitkubCredentialsFile.Read(Path.Combine(project, ".env"));
            var fingerprint = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "\0" + secret)));
            var useReceipts = captures.Length == 1;
            var profile = useReceipts ? BitkubProfileVerifier.ForOrderReceipts(evidence[0], fingerprint, DateTimeOffset.UtcNow)
                : BitkubProfileVerifier.Verify(evidence[0], evidence[1], evidence[2], fingerprint, DateTimeOffset.UtcNow);
            // Recheck the actual key/permission/wallet before preparing execution configuration.
            using var broker = new BitkubLiveBroker(key, secret);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            if (!await broker.CheckTradePermissionAsync(deadline.Token)) throw new InvalidDataException();
            var first = await broker.GetWalletAsync(deadline.Token);
            var second = await broker.GetWalletAsync(deadline.Token);
            using var finalCapture = JsonDocument.Parse(evidence[^1]);
            var expectedCash = finalCapture.RootElement.GetProperty("thbTotal").GetDecimal();
            var expectedWallet = JsonSerializer.Deserialize<BrokerWalletBalance[]>(finalCapture.RootElement.GetProperty("walletBalances"))!;
            if (!first.OrderBy(b => b.Currency).SequenceEqual(second.OrderBy(b => b.Currency)) ||
                !first.OrderBy(b => b.Currency).SequenceEqual(expectedWallet.OrderBy(b => b.Currency)) ||
                first.Any(b => b.Reserved != 0 || !useReceipts && b.Currency != "THB" && b.Total != 0) ||
                first.Single(b => b.Currency == "THB").Total != expectedCash || (!useReceipts && expectedCash <= 0) ||
                !(await File.ReadAllBytesAsync(settingsPath)).SequenceEqual(settingsBytes)) throw new InvalidDataException();
            foreach (var symbol in new[] { "BTC-THB", "ETH-THB", "SOL-THB" })
                if ((await broker.GetOpenOrdersAsync(symbol, deadline.Token)).Count != 0) throw new InvalidDataException();
            // Corroborate the saved records against fresh authenticated broker reads.
            var expectedHistory = JsonSerializer.Deserialize<BrokerHistoryEntry[]>(finalCapture.RootElement.GetProperty("orderHistory"))!;
            var history = await broker.GetHistoryAsync("BTC-THB", deadline.Token);
            if (!history.OrderBy(h => h.TransactionId).SequenceEqual(expectedHistory.OrderBy(h => h.TransactionId)))
                throw new InvalidDataException();
            var expectedOrders = JsonSerializer.Deserialize<BrokerOrderEvidence[]>(finalCapture.RootElement.GetProperty("orderDetails"))!;
            foreach (var expected in expectedOrders)
            {
                var observed = await broker.GetOrderAsync(expected.Symbol, expected.OrderId, expected.Side, deadline.Token);
                if (expected with { History = observed.History } != observed ||
                    !expected.History.OrderBy(h => h.TransactionId).SequenceEqual(observed.History.OrderBy(h => h.TransactionId)))
                    throw new InvalidDataException();
            }
            var market = await broker.GetSymbolRulesAsync("BTC-THB", deadline.Token);
            var expectedMarket = JsonSerializer.Deserialize<BrokerSymbolRules[]>(finalCapture.RootElement.GetProperty("marketRules"))!
                .Single(p => p.Symbol == "BTC-THB");
            if (market with { ObservedAt = expectedMarket.ObservedAt } != expectedMarket) throw new InvalidDataException();
            if (useReceipts)
            {
                var subscribed = false;
                try
                {
                    await broker.ObserveOrdersAsync((_, _) => Task.CompletedTask,
                        value => { if (value) { subscribed = true; deadline.Cancel(); } }, deadline.Token);
                }
                catch (OperationCanceledException) when (subscribed) { }
                if (!subscribed) throw new InvalidDataException();
            }
            var current = BitkubCredentialsFile.Read(Path.Combine(project, ".env"));
            if (current != (key, secret) || !(await File.ReadAllBytesAsync(settingsPath)).SequenceEqual(settingsBytes)) throw new InvalidDataException();
            var profilePath = Path.Combine(runtime, "live", "bitkub-verification.json");
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            // Serializes cooperating verifier processes; retain the empty sidecar so
            // unlocking cannot race with replacement of another process's lock file.
            using var profileLock = new FileStream(profilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(profilePath))
            {
                using var previous = JsonDocument.Parse(await File.ReadAllBytesAsync(profilePath));
                if (previous.RootElement.TryGetProperty("Rules", out var rules) && rules.ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException();
            }
            var pending = profilePath + ".pending-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(pending, profilePath, overwrite: true);
            Console.WriteLine(useReceipts ? "BTC Spot profile prepared with authenticated order receipts. Existing cash/coins retained; execution remains disabled. No order was sent."
                : "Verified BTC Spot profile saved from matching broker captures. Execution remains disabled; no order was sent.");
            return 0;
        }
        catch (InvalidDataException error) when (error.Message == "BITKUB_BUY_CASH_FEE_NOT_PROVEN")
        {
            Console.WriteLine("Profile not saved: the buy fee was fully covered by credits, so cash-fee accounting is not proven. Obtain paid-fee evidence or Bitkub confirmation; do not repeat trades merely to exhaust credits. Execution remains disabled.");
            return 1;
        }
        catch
        {
            // No raw captures, credential identifiers, config/connection data or provider errors.
            Console.WriteLine("Profile not saved: evidence, current wallet/permission or inactive configuration did not verify. No order was sent.");
            return 1;
        }
    }
}
