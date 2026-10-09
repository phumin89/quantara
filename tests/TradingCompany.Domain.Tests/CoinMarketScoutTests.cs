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
        Assert.Throws<ArgumentOutOfRangeException>(() => CoinMarketScout.Rank([], [], [], 1, -1));
        Assert.Throws<InvalidDataException>(() => CoinMarketScout.Rank([], ["bad"], [], 1));
        Assert.Throws<InvalidDataException>(() => CoinMarketScout.Rank([], ["XRP-THB\nIGNORE"], [], 1));
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

    [Fact]
    public void Broad_execution_membership_does_not_fill_the_research_batch_with_mandatory_markets()
    {
        var symbols = Enumerable.Range(0, 12).Select(i => $"M{i:00}-THB").ToArray();
        var rows = symbols.Select((s, i) => new CoinMarketObservation(s, true, false, 10, 10.01m, 200_000 + i * 1000, 0)).ToArray();
        var result = CoinMarketScout.Rank(rows, symbols, ["M00-THB"], 1000);
        Assert.Equal(8, result.Count);
        Assert.Equal("M00-THB", result[0].Symbol);
        Assert.True(result[0].Held);
        Assert.Equal(symbols.Reverse().Take(7), result.Skip(1).Select(c => c.Symbol));
        Assert.All(result, c => Assert.True(c.ExecutionAllowed));
        Assert.Equal(12, symbols.Length);
    }

    [Fact]
    public void Eligible_kub_is_prioritized_without_bypassing_filters_or_execution_membership()
    {
        var rows = new[]
        {
            new CoinMarketObservation("BTC-THB", true, false, 10, 10.01m, 2_000_000, 0),
            new CoinMarketObservation("KUB-THB", true, false, 10, 10.01m, 100_000, 0),
            new CoinMarketObservation("FRO-THB", true, true, 10, 10.01m, 3_000_000, 0),
            new CoinMarketObservation("THI-THB", true, false, 10, 10.01m, 99_999, 0),
            new CoinMarketObservation("BAD-THB", true, false, 10, 11, 3_000_000, 0)
        };

        var markets = rows.Concat(Enumerable.Range(0, 10).Select(i =>
            new CoinMarketObservation($"C{i:00}-THB", true, false, 10, 10.01m, 1_000_000, 0))).ToArray();
        var result = CoinMarketScout.Rank(markets, ["BTC-THB", "AAA-THB", "BBB-THB", "CCC-THB"], [], 1_000);

        Assert.Equal(8, result.Count);
        Assert.Equal("KUB-THB", result[0].Symbol);
        Assert.False(result[0].ExecutionAllowed);
        Assert.DoesNotContain(result, c => c.Symbol is "FRO-THB" or "THI-THB" or "BAD-THB");
    }

    [Theory]
    [InlineData(false, false, 10, 10.01, 200000)]
    [InlineData(true, true, 10, 10.01, 200000)]
    [InlineData(true, false, 10, 10.01, 99999)]
    [InlineData(true, false, 10, 11, 200000)]
    [InlineData(true, false, 0, 10, 200000)]
    [InlineData(true, false, 10, 9, 200000)]
    public void Preferred_kub_must_still_pass_the_existing_market_filters(bool active, bool frozen,
        double bid, double ask, int volume)
    {
        CoinMarketObservation[] rows = [new("BTC-THB", true, false, 10, 10.01m, 200000, 0),
            new("KUB-THB", active, frozen, (decimal)bid, (decimal)ask, volume, 20)];
        var result = CoinMarketScout.Rank(rows, ["BTC-THB", "AAA-THB", "BBB-THB", "CCC-THB"], [], 1000);
        Assert.Equal("BTC-THB", Assert.Single(result).Symbol);
    }

    [Fact]
    public void More_than_eight_holdings_use_a_deterministic_batch_without_rejecting_the_portfolio()
    {
        var symbols = Enumerable.Range(0, 10).Select(i => $"M{i:00}-THB").ToArray();
        var rows = symbols.Select(s => new CoinMarketObservation(s, true, false, 10, 10.01m, 200_000, 0)).ToArray();
        var result = CoinMarketScout.Rank(rows.Reverse().ToArray(), symbols, symbols.Reverse().ToArray(), 1000);
        Assert.Equal(symbols.Take(8), result.Select(c => c.Symbol));
        Assert.All(result, c => Assert.True(c.Held));
        Assert.Equal(10, symbols.Length);
    }

    [Fact]
    public void Twelve_held_markets_rotate_across_batches_while_preserving_the_best_new_opportunity()
    {
        var held = Enumerable.Range(0, 12).Select(i => $"H{i:00}-THB").ToArray();
        var rows = held.Select(s => new CoinMarketObservation(s, true, false, 10, 10.01m, 1, 0))
            .Concat([new("XRP-THB", true, false, 10, 10.01m, 500_000, 0),
                new("DOGE-THB", true, false, 10, 10.01m, 400_000, 0),
                new("FROZEN-THB", true, true, 10, 10.01m, 900_000, 0)]).ToArray();
        var execution = rows.Select(r => r.Symbol).ToArray();
        var first = CoinMarketScout.Rank(rows, execution, held, 1000, 0);
        var second = CoinMarketScout.Rank(rows.Reverse().ToArray(), execution.Reverse().ToArray(), held.Reverse().ToArray(), 1000, 1);
        Assert.Equal(held.Take(7).Append("XRP-THB"), first.Select(c => c.Symbol));
        Assert.Equal(held.Skip(7).Concat(held.Take(2)).Append("XRP-THB"), second.Select(c => c.Symbol));
        Assert.Equal(held, first.Concat(second).Where(c => c.Held).Select(c => c.Symbol).Distinct().Order(StringComparer.Ordinal));
        Assert.All(new[] { first, second }, batch =>
        {
            Assert.Equal(8, batch.Count);
            Assert.Equal("XRP-THB", Assert.Single(batch, c => !c.Held).Symbol);
        });
        Assert.Equal(first, CoinMarketScout.Rank(rows, execution, held, 1000, 0));
        Assert.Equal(12, held.Length);
    }

    [Fact]
    public void Seven_held_markets_and_legacy_mandatory_membership_do_not_rotate()
    {
        var symbols = Enumerable.Range(0, 9).Select(i => $"M{i:00}-THB").ToArray();
        var rows = symbols.Select((s, i) => new CoinMarketObservation(s, true, false, 10, 10.01m, 200_000 + i, 0)).ToArray();
        Assert.Equal(CoinMarketScout.Rank(rows, symbols, symbols.Take(7).ToArray(), 1000, 0),
            CoinMarketScout.Rank(rows, symbols, symbols.Take(7).ToArray(), 1000, 1));
        Assert.Equal(CoinMarketScout.Rank(rows, symbols.Take(3).ToArray(), [], 1000, 0),
            CoinMarketScout.Rank(rows, symbols.Take(3).ToArray(), [], 1000, 1));
    }
}
