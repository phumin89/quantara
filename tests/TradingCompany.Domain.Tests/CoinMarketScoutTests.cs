using System.IO;
using TradingCompany.Application;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CoinMarketScoutTests
{
    [Fact]
    public void Ranks_liquid_markets_deterministically_and_preserves_mandatory()
    {
        var rows = Enumerable.Range(0, 10).Select(i => new CoinMarketObservation($"C{i:00}-THB", true, false, 10, 10.01m, 2_000_000 - i * 1000, i)).ToArray();
        var result = CoinMarketScout.Rank(rows, ["C09-THB"], ["C08-THB"], 1000);
        Assert.Equal(7, result.Count);
        Assert.Equal(["C08-THB", "C09-THB"], result.Take(2).Select(x => x.Symbol));
        Assert.Equal(0m, result[0].DepthQuoteNotional);
    }

    [Fact]
    public void Excludes_frozen_thin_and_bad_quotes_but_keeps_mandatory_quote()
    {
        var rows = new[]
        {
            new CoinMarketObservation("BTC-THB", true, true, 10, 11, 1, 0),
            new CoinMarketObservation("ETH-THB", true, false, 10, 10.1m, 99_999, 0),
            new CoinMarketObservation("SOL-THB", true, false, 10, 10.01m, 200_000, 0),
            new CoinMarketObservation("XRP-THB", false, false, 10, 10.01m, 200_000, 0)
        };
        var result = CoinMarketScout.Rank(rows, ["BTC-THB"], [], 1000);
        Assert.Equal(["BTC-THB", "SOL-THB"], result.Select(x => x.Symbol));
    }

    [Fact]
    public void Rejects_invalid_symbols_duplicates_and_nonpositive_notional()
    {
        Assert.Throws<InvalidDataException>(() => CoinMarketScout.Rank([new("btc-THB", true, false, 1, 1, 1, 0)], [], [], 1));
        Assert.Throws<InvalidDataException>(() => CoinMarketScout.Rank([new("BTC-THB", true, false, 1, 1, 1, 0), new("BTC-THB", true, false, 1, 1, 1, 0)], [], [], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoinMarketScout.Rank([], [], [], 0));
        Assert.Throws<InvalidDataException>(() => CoinMarketScout.Rank([], ["bad"], [], 1));
        Assert.Throws<InvalidDataException>(() => CoinMarketScout.Rank([], ["AA-THB", "BB-THB"], ["CC-THB", "DD-THB"], 1));
    }

    [Fact]
    public void Caps_three_mandatory_plus_five_ranked_and_ignores_decimal_min_change()
    {
        var mandatory = new[] { "AA-THB", "BB-THB", "CC-THB" }.Select(s => new CoinMarketObservation(s, true, false, 1, 1.001m, 1, 0));
        var scouts = Enumerable.Range(0, 7).Select(i => new CoinMarketObservation($"S{i:00}-THB", true, false, 1, 1.001m, 2_000_000 - i, i == 0 ? decimal.MinValue : 1));
        var result = CoinMarketScout.Rank(mandatory.Concat(scouts).ToArray(), ["CC-THB"], ["AA-THB", "BB-THB"], 1_000);
        Assert.Equal(8, result.Count);
        Assert.DoesNotContain(result, x => x.Symbol == "S00-THB");
    }
}
