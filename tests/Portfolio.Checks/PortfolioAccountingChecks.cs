using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class PortfolioAccountingChecks
{
    private static readonly DateTimeOffset startedAt = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);

    public static async Task Run()
    {
        await WeightedCostsAndFees();
        await ClosedAndReopened();
        await BrokerPrecision();
        await DuplicateEvidence();
        await InvalidEvidenceIsolatesSymbols();
        await UnreconciledHoldings();
        await HistoricalResidualQuarantine(.00001902m);
        await HistoricalResidualQuarantine(268.30338665m);
        await UnavailableQuotes();
        await ImportedHistoryRemainsUnknown();
        await FreshAccountSnapshot();
        Console.WriteLine(
            "Portfolio accounting checks passed: confirmed engine scope, exact fees and truthful unknowns."
        );
    }

    private static async Task WeightedCostsAndFees()
    {
        var live = PartialRoundTrip();
        var wallet = Wallet("ETH", 2, 150) with
        {
            Trades =
            [
                new("buy-one", "ETH", TradeSide.Buy, 2, 100, 3, 1, startedAt),
                new("buy-two", "ETH", TradeSide.Buy, 1, 160, 1, 0, startedAt.AddMinutes(1)),
                new("sell-one", "ETH", TradeSide.Sell, 1, 180, 1, 0, startedAt.AddMinutes(2)),
            ],
        };
        var paper = Ledger(Fill("paper", "ETH", TradeSide.Buy, 1, 999, 10, 3));
        var view = await View(live, wallet, paper);
        var position = view.Positions.Single();
        Check(
            position.AverageCost == 121m
                && position.RealizedPnl == 58m
                && position.UnrealizedPnl == 58m
                && position.CostStatus.ToString() == "Verified",
            "Confirmed live fills must release weighted cost including paid entry fees and deduct exit fees."
        );
        var performance = Performance(view);
        Check(
            performance.GetProperty("scope").GetString() == "confirmedEngineFills"
                && Amount(performance, "realizedPnl") == 58m
                && Amount(performance, "unrealizedPnl") == 58m
                && Amount(performance, "totalPnl") == 116m
                && Amount(performance, "fees") == 4m
                && performance.GetProperty("unreconciledSymbols").GetArrayLength() == 0
                && !string.IsNullOrWhiteSpace(performance.GetProperty("notice").GetString()),
            "Performance must explicitly describe engine-only scope without counting imported or paper fills twice."
        );
        Check(
            live.Positions.Single().CostThb == 242m && live.Fills.Count == 3,
            "Portfolio accounting queries must leave retained engine evidence unchanged."
        );
    }

    private static async Task ClosedAndReopened()
    {
        var closed = Ledger(
            Fill("closed-buy", "ETH", TradeSide.Buy, 2, 100, 2, 0),
            Fill("closed-sell", "ETH", TradeSide.Sell, 2, 150, 2, 1)
        );
        var closedView = await View(closed, Wallet("ETH", 0, null));
        Check(
            closedView.Positions.Single().RealizedPnl == 96m
                && closedView.Positions.Single().UnrealizedPnl == 0m
                && Amount(Performance(closedView), "totalPnl") == 96m,
            "A verified fully closed engine position retains realized profit without requiring a quote."
        );
        var reopened = TradingLedgerAccounting.Apply(closed, Fill("reopened-buy", "ETH", TradeSide.Buy, 1, 90, 1, 2));
        var view = await View(reopened, Wallet("ETH", 1, 110));
        Check(
            view.Positions.Single().AverageCost == 91m
                && view.Positions.Single().RealizedPnl == 96m
                && view.Positions.Single().UnrealizedPnl == 19m
                && Amount(Performance(view), "totalPnl") == 115m
                && Amount(Performance(view), "fees") == 5m,
            "Reopening resets holding cost while realized engine profit and fees span all verified cycles."
        );
    }

    private static async Task BrokerPrecision()
    {
        const decimal rawQuantity = 1877.22m / 43.34m;
        const decimal creditedQuantity = 43.31379787m;
        var buy = Fill("precision-buy", "XRP", TradeSide.Buy, rawQuantity, 43.34m, 0, 0) with
        {
            GrossThb = 1877.22m,
            QuantityScale = 8,
        };
        var live = Ledger(buy);
        var open = await View(live, Wallet("XRP", creditedQuantity, 44m));
        Check(
            open.Positions.Single().AverageCost == 1877.22m / creditedQuantity
                && open.Positions.Single().UnrealizedPnl == creditedQuantity * 44m - 1877.22m,
            "Retained broker base precision must determine credited holding quantity without rewriting raw fill evidence."
        );
        var legacy = Ledger(buy with { QuantityScale = null });
        var sell = Fill("precision-sell", "XRP", TradeSide.Sell, creditedQuantity, 42.96m, 1, 1) with
        {
            QuantityScale = 8,
        };
        var closed = TradingLedgerAccounting.Apply(legacy, sell);
        var view = await View(closed, Wallet("XRP", 0, null));
        Check(
            view.Positions.Single().RealizedPnl == sell.GrossThb - 1m - 1877.22m
                && view.Positions.Single().UnrealizedPnl == 0m,
            "A confirmed precision-limited full exit releases legacy division residue and the entire original cost."
        );
    }

    private static async Task DuplicateEvidence()
    {
        var live = PartialRoundTrip();
        var duplicate = live with
        {
            Fills = [.. live.Fills, live.Fills[0] with { Id = Guid.NewGuid() }, live.Fills[2]],
        };
        var view = await View(duplicate, Wallet("ETH", 2, 150));
        Check(
            view.Positions.Single().AverageCost == 121m
                && view.Positions.Single().RealizedPnl == 58m
                && Amount(Performance(view), "fees") == 4m,
            "Repeated identical broker fill identities must never double-count quantity, cost, fees or realized profit."
        );
    }

    private static async Task InvalidEvidenceIsolatesSymbols()
    {
        var good = Ledger(
            Fill("btc-buy", "BTC", TradeSide.Buy, 1, 100, 1, 0),
            Fill("btc-sell", "BTC", TradeSide.Sell, 0.5m, 120, 1, 1)
        );
        var badBuy = Fill("eth-buy", "ETH", TradeSide.Buy, 2, 100, 2, 2) with { GrossFeeThb = 3, FeeCreditThb = 1 };
        var both = TradingLedgerAccounting.Apply(good, badBuy);
        var variants = new[]
        {
            both with
            {
                Fills = [.. both.Fills, badBuy with { GrossFeeThb = 4, FeeCreditThb = 2 }],
            },
            both with
            {
                Fills = [.. both.Fills, Fill("invalid", "ETH", TradeSide.Buy, -1, 100, 1, 3)],
            },
            good with
            {
                Fills = [.. good.Fills, Fill("missing-buy", "ETH", TradeSide.Sell, 1, 100, 1, 2)],
            },
        };
        foreach (var live in variants)
        {
            var wallet = Wallet("BTC", 0.5m, 130) with
            {
                Coins = [new("BTC", "Bitcoin"), new("ETH", "Ethereum")],
                Balances = [new("BTC", 0.5m), new("ETH", 2)],
                Prices = [new("BTC", 130), new("ETH", 150)],
            };
            var view = await View(live, wallet);
            var btc = view.Positions.Single(item => item.Symbol == "BTC");
            var eth = view.Positions.Single(item => item.Symbol == "ETH");
            Check(
                btc.AverageCost == 101m
                    && btc.RealizedPnl == 8.5m
                    && btc.UnrealizedPnl == 14.5m
                    && eth.AverageCost is null
                    && eth.RealizedPnl is null
                    && eth.UnrealizedPnl is null
                    && eth.CostStatus == CostStatus.Unknown,
                "Conflicting, malformed or missing-basis fills must invalidate only their symbol and preserve verified peers."
            );
            var performance = Performance(view);
            Check(
                Amount(performance, "realizedPnl") is null
                    && Amount(performance, "unrealizedPnl") is null
                    && Amount(performance, "totalPnl") is null
                    && Amount(performance, "fees") is null
                    && Unreconciled(performance, "ETH"),
                "Incomplete engine evidence must not expose a partial sum as complete engine performance."
            );
        }
    }

    private static async Task UnreconciledHoldings()
    {
        var live = PartialRoundTrip();
        var wallet = Wallet("ETH", 2, 150);
        var retainedVariants = new[]
        {
            live with
            {
                Positions = [live.Positions.Single() with { Quantity = 3m }],
            },
            live with
            {
                Positions = [live.Positions.Single() with { CostThb = 243m }],
            },
            live with
            {
                Positions = [],
            },
        };
        foreach (var retained in retainedVariants)
        {
            CheckUnreconciled(await View(retained, wallet));
        }

        foreach (
            var unverifiedWallet in new[]
            {
                wallet with
                {
                    Balances = [new("ETH", 3)],
                },
                wallet with
                {
                    UpdatedAt = startedAt.AddMinutes(1),
                },
                wallet with
                {
                    UpdatedAt = null,
                },
            }
        )
        {
            CheckUnreconciled(await View(live, unverifiedWallet));
        }
    }

    private static async Task UnavailableQuotes()
    {
        var live = PartialRoundTrip();
        var wallet = Wallet("ETH", 2, 150);
        foreach (
            var unquotedWallet in new[]
            {
                wallet with
                {
                    Prices = [],
                },
                wallet with
                {
                    PriceUpdatedAt = null,
                },
                wallet with
                {
                    PriceUpdatedAt = startedAt.AddMinutes(1),
                },
                wallet with
                {
                    PriceUpdatedAt = startedAt.AddHours(3),
                },
            }
        )
        {
            var view = await View(live, unquotedWallet);
            var position = view.Positions.Single();
            var performance = Performance(view);
            Check(
                position.AverageCost == 121m
                    && position.RealizedPnl == 58m
                    && position.UnrealizedPnl is null
                    && Amount(performance, "realizedPnl") == 58m
                    && Amount(performance, "unrealizedPnl") is null
                    && Amount(performance, "totalPnl") is null
                    && Amount(performance, "fees") == 4m,
                "Unavailable, stale or future quotes must preserve verified cost and realized profit without inventing valuation."
            );
        }
    }

    private static async Task HistoricalResidualQuarantine(decimal quantity)
    {
        var residual = new TradingHolding("ETH", quantity, 1m, startedAt, 100m);
        var retained = Ledger() with { Positions = [residual] };
        var wallet = Wallet("ETH", 0m, null);
        var view = await View(retained, wallet);
        var position = view.Positions.Single();
        var performance = Performance(view);
        Check(
            position.Quantity == 0m
                && position.AverageCost is null
                && position.CostStatus == CostStatus.Unknown
                && position.RealizedPnl is null
                && position.UnrealizedPnl is null
                && view.Summary.HoldingCount == 0
                && Amount(performance, "realizedPnl") is null
                && Amount(performance, "unrealizedPnl") is null
                && Amount(performance, "totalPnl") is null
                && performance.GetProperty("unreconciledSymbols").EnumerateArray().Single().GetString() == "ETH"
                && retained.Positions.Single() == residual
                && retained.Fills.Count == 0,
            "A zero broker wallet remains zero holdings while a historical residual is quarantined as unreconciled accounting, never fabricated profit, fill, or a written-off lot."
        );
    }

    private static async Task ImportedHistoryRemainsUnknown()
    {
        var wallet = Wallet("ETH", 1, 150) with
        {
            Trades = [new("imported", "ETH", TradeSide.Buy, 1, 100, 1, 0, startedAt)],
        };
        var view = await View(Ledger(), wallet);
        var position = view.Positions.Single();
        var performance = Performance(view);
        Check(
            position.AverageCost is null
                && position.RealizedPnl is null
                && position.UnrealizedPnl is null
                && position.CostStatus == CostStatus.Unknown
                && performance.GetProperty("scope").GetString() == "confirmedEngineFills"
                && Amount(performance, "realizedPnl") == 0m
                && Amount(performance, "fees") == 0m,
            "Imported-only holdings keep unknown full-wallet basis even when the explicitly engine-only fill total is zero."
        );
    }

    private static void CheckUnreconciled(PortfolioView view)
    {
        var position = view.Positions.Single();
        var performance = Performance(view);
        Check(
            position.AverageCost is null
                && position.UnrealizedPnl is null
                && position.CostStatus == CostStatus.Unknown
                && position.RealizedPnl == 58m
                && Amount(performance, "realizedPnl") == 58m
                && Amount(performance, "unrealizedPnl") is null
                && Amount(performance, "totalPnl") is null
                && Unreconciled(performance, "ETH"),
            "Wallet and retained holding quantity/cost must reconcile with fill replay before exposing holding cost."
        );
    }

    private static async Task FreshAccountSnapshot()
    {
        var live = PartialRoundTrip();
        var oldWallet = Wallet("ETH", 3, 140);
        var capital = new CapitalSnapshot(
            "account-one",
            startedAt.AddHours(2).AddSeconds(-1),
            600m,
            1300m,
            300m,
            300m,
            [new("THB", 1000m, 700m, 1m, 0m), new("ETH", 2m, 1.5m, 150m, 0.5m)],
            false,
            null
        );
        capital = capital with { Source = CapitalSource.Broker };
        var autonomy = new AutonomousTradingStatus(
            capital.ObservedAt,
            null,
            1,
            1,
            [],
            capital,
            "observed",
            capital.ObservedAt
        );
        var state = TradingStateFactory.Create(startedAt) with { Live = live, Autonomy = autonomy };
        var fresh = await View(state, oldWallet, "account-one");
        Check(
            fresh.Positions.Single().Quantity == 2m
                && fresh.Positions.Single().AverageCost == 121m
                && fresh.Positions.Single().UnrealizedPnl == 58m
                && fresh.Summary.TotalAssets == 300m
                && fresh.Summary.AvailableThb == 700m
                && fresh.UpdatedAt == capital.ObservedAt
                && fresh.PriceUpdatedAt == capital.ObservedAt
                && fresh.HistoryUpdatedAt == oldWallet.HistoryUpdatedAt,
            "A newer verified same-account snapshot uses total assets, exact available cash and observed bid quotes without subtracting reserves twice."
        );
        foreach (
            var unavailable in new[]
            {
                capital with
                {
                    Source = CapitalSource.Unknown,
                },
                capital with
                {
                    Source = CapitalSource.Paper,
                },
                capital with
                {
                    AccountKey = "another-account",
                },
                capital with
                {
                    ObservedAt = startedAt.AddHours(2).AddMinutes(-2),
                },
                capital with
                {
                    ObservedAt = startedAt.AddHours(2).AddSeconds(1),
                },
                capital with
                {
                    ObservedAt = oldWallet.UpdatedAt.GetValueOrDefault(),
                },
                capital with
                {
                    Blocker = "unavailable evidence",
                },
                capital with
                {
                    HasUncertainOrders = true,
                },
            }
        )
        {
            var blocked = state with { Autonomy = autonomy with { Capital = unavailable } };
            var view = await View(blocked, oldWallet, "account-one");
            Check(
                view.Positions.Single().Quantity == 3m
                    && view.Positions.Single().AverageCost is null
                    && view.UpdatedAt == oldWallet.UpdatedAt,
                "Foreign, stale, future, older or blocked capital snapshots must never replace retained account data."
            );
        }

        var anonymous = await View(state, oldWallet, null);
        Check(
            anonymous.Positions.Single().Quantity == 3m && anonymous.Positions.Single().AverageCost is null,
            "A capital snapshot cannot reconcile the portfolio without an explicit matching account context."
        );
    }

    private static TradingLedger PartialRoundTrip() =>
        Ledger(
            Fill("buy-one", "ETH", TradeSide.Buy, 2, 100, 2, 0) with
            {
                GrossFeeThb = 3,
                FeeCreditThb = 1,
            },
            Fill("buy-two", "ETH", TradeSide.Buy, 1, 160, 1, 1),
            Fill("sell-one", "ETH", TradeSide.Sell, 1, 180, 1, 2)
        );

    private static TradingLedger Ledger(params TradingFill[] fills)
    {
        var ledger = TradingStateFactory.Create(startedAt).Live with { FundingThb = 1_000_000m, CashThb = 1_000_000m };
        return fills.Aggregate(ledger, TradingLedgerAccounting.Apply);
    }

    private static TradingFill Fill(
        string brokerFillId,
        string symbol,
        TradeSide side,
        decimal quantity,
        decimal price,
        decimal fee,
        int minute
    ) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            brokerFillId,
            symbol,
            side,
            quantity,
            price,
            quantity * price,
            fee,
            startedAt.AddMinutes(minute)
        );

    private static PortfolioLedger Wallet(string symbol, decimal quantity, decimal? price) =>
        new()
        {
            Coins = [new(symbol, symbol)],
            Balances = quantity == 0 ? [] : [new(symbol, quantity)],
            Prices = price is { } current ? [new(symbol, current)] : [],
            UpdatedAt = startedAt.AddHours(1),
            PriceUpdatedAt = startedAt.AddHours(1),
            HistoryUpdatedAt = startedAt.AddHours(1),
        };

    private static Task<PortfolioView> View(TradingLedger live, PortfolioLedger wallet, TradingLedger? paper = null)
    {
        var state = TradingStateFactory.Create(startedAt);
        return View(state with { Live = live, Paper = paper ?? state.Paper }, wallet, null);
    }

    private static Task<PortfolioView> View(TradingState state, PortfolioLedger wallet, string? accountKey)
    {
        return new GetPortfolioHandler(
            new PortfolioTestRepository(wallet),
            new PortfolioTestSync(),
            new TradingTestRepository(state),
            new FixedTimeProvider(startedAt.AddHours(2)),
            accountKey is null ? null : new AccountContext(accountKey)
        )
            .Handle(new GetPortfolioQuery(), CancellationToken.None)
            .AsTask();
    }

    private static JsonElement Performance(PortfolioView view)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        PortfolioJson.ConfigureOptions(options);
        var json = JsonSerializer.SerializeToElement(view, options);
        Check(
            json.TryGetProperty("performance", out var performance) && performance.ValueKind == JsonValueKind.Object,
            "Portfolio API must expose explicitly scoped confirmed engine performance."
        );
        return performance;
    }

    private static decimal? Amount(JsonElement performance, string name) =>
        performance.GetProperty(name) is { ValueKind: JsonValueKind.Number } amount ? amount.GetDecimal() : null;

    private static bool Unreconciled(JsonElement performance, string symbol) =>
        performance.GetProperty("unreconciledSymbols").EnumerateArray().Any(item => item.GetString() == symbol);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
