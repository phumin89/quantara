using System.Collections.Concurrent;
using System.Text.Json;

namespace TradingCompany.Api;

public sealed record HistoricalUsdThbRate(DateOnly Date, decimal Rate);
public interface IUsdThbRateProvider
{
    Task<decimal> GetAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HistoricalUsdThbRate>> GetHistoricalAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken);
}

public sealed class FrankfurterUsdThbRateProvider(IHttpClientFactory clients) : IUsdThbRateProvider
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private decimal cachedRate;
    private DateTimeOffset expiresAt;
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<HistoricalUsdThbRate>>> historicalCache = new();

    public async Task<decimal> GetAsync(CancellationToken cancellationToken)
    {
        if (cachedRate > 0m && expiresAt > DateTimeOffset.UtcNow) return cachedRate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cachedRate > 0m && expiresAt > DateTimeOffset.UtcNow) return cachedRate;
            using var response = await clients.CreateClient("market-data").GetAsync("https://api.frankfurter.dev/v2/rate/USD/THB", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (!json.RootElement.TryGetProperty("rate", out var value) || !value.TryGetDecimal(out var rate) || rate <= 0m)
                throw new InvalidDataException("Frankfurter returned an invalid USD/THB rate.");
            cachedRate = rate; expiresAt = DateTimeOffset.UtcNow.AddHours(1); return rate;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<HistoricalUsdThbRate>> GetHistoricalAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        if (end < start) throw new ArgumentException("End date must not precede start date.");
        var key = $"{start:yyyy-MM-dd}:{end:yyyy-MM-dd}";
        var task = historicalCache.GetOrAdd(key, _ => LoadHistoricalAsync(start, end, CancellationToken.None));
        try { return await task.WaitAsync(cancellationToken); }
        catch
        {
            if (task.IsFaulted || task.IsCanceled) historicalCache.TryRemove(new KeyValuePair<string, Task<IReadOnlyList<HistoricalUsdThbRate>>>(key, task));
            throw;
        }
    }

    private async Task<IReadOnlyList<HistoricalUsdThbRate>> LoadHistoricalAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clients.CreateClient("market-data").GetAsync($"https://api.frankfurter.dev/v2/rates?base=USD&quotes=THB&from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Frankfurter returned an invalid historical rate series.");
            var rows = json.RootElement.EnumerateArray().Select(row =>
            {
                if (!row.TryGetProperty("date", out var dateValue) || !DateOnly.TryParse(dateValue.GetString(), out var date) ||
                    !row.TryGetProperty("base", out var baseValue) || baseValue.GetString() != "USD" ||
                    !row.TryGetProperty("quote", out var quoteValue) || quoteValue.GetString() != "THB" ||
                    !row.TryGetProperty("rate", out var rateValue) || !rateValue.TryGetDecimal(out var rate) || rate <= 0m)
                    throw new InvalidDataException("Frankfurter returned an invalid historical USD/THB rate.");
                return new HistoricalUsdThbRate(date, rate);
            }).ToArray();
            if (rows.Any(x => x.Date < start || x.Date > end) || rows.Zip(rows.Skip(1)).Any(x => x.First.Date >= x.Second.Date))
                throw new InvalidDataException("Frankfurter returned unordered or out-of-range historical rates.");
            return rows;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Frankfurter returned malformed historical rate data.", ex); }
    }
}
