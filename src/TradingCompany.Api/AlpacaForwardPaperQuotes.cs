using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TradingCompany.Application;

namespace TradingCompany.Api;

public sealed class AlpacaForwardPaperQuotes(
    IHttpClientFactory clients, IUsdThbRateProvider rates, IConfiguration configuration) : IForwardPaperQuotes
{
    private static readonly string[] Symbols = ["NVDA", "AAPL", "MSFT", "VOO", "QQQ"];
    public IReadOnlyList<string> SupportedSymbols => Symbols;
    public bool CredentialsConfigured => !string.IsNullOrWhiteSpace(configuration["APCA_API_KEY_ID"]) &&
        !string.IsNullOrWhiteSpace(configuration["APCA_API_SECRET_KEY"]);

    public async Task<ForwardPaperQuote?> GetAsync(string symbol, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!Symbols.Contains(symbol, StringComparer.Ordinal) || !CredentialsConfigured) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://data.alpaca.markets/v2/stocks/{symbol}/quotes/latest?feed=iex");
            request.Headers.TryAddWithoutValidation("APCA-API-KEY-ID", configuration["APCA_API_KEY_ID"]);
            request.Headers.TryAddWithoutValidation("APCA-API-SECRET-KEY", configuration["APCA_API_SECRET_KEY"]);
            using var response = await clients.CreateClient("market-data").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = json.RootElement;
            if (!root.TryGetProperty("symbol", out var symbolValue) || symbolValue.GetString() != symbol ||
                !root.TryGetProperty("quote", out var quote) || !quote.TryGetProperty("ap", out var askValue) ||
                !quote.TryGetProperty("bp", out var bidValue) || !quote.TryGetProperty("t", out var timeValue) ||
                !askValue.TryGetDecimal(out var ask) || !bidValue.TryGetDecimal(out var bid) ||
                !DateTimeOffset.TryParse(timeValue.GetString(), out var observed) || bid <= 0m || ask < bid ||
                observed > now || now - observed > TimeSpan.FromMinutes(30)) return null;
            var date = DateOnly.FromDateTime(observed.UtcDateTime);
            var historical = await rates.GetHistoricalAsync(date.AddDays(-7), date, cancellationToken);
            var fx = historical.Where(x => x.Date <= date).OrderByDescending(x => x.Date).FirstOrDefault();
            if (fx is null || fx.Rate <= 0m || date.DayNumber - fx.Date.DayNumber > 7) return null;
            return new(symbol, (bid + ask) / 2m, "USD", fx.Rate, observed, now, fx.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                "alpaca-iex-paper-spread", 0m, (ask - bid) / 2m * fx.Rate);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or HttpRequestException or InvalidOperationException or OverflowException)
        { return null; }
    }
}
