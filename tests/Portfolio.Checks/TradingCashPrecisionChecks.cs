using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingCashPrecisionChecks
{
    public static async Task Run(TradingState adopted, string account)
    {
        await Activation(adopted, account, false, 0m, true);
        await Activation(adopted, account, true, 0m, true);
        await Activation(adopted, account, true, -0.01m, false);
        await Activation(adopted, account, false, 0.01m, false);

        var now = adopted.Live.CashBaselines[^1].Cutoff.AddMinutes(10);
        var ledger = WithExecutions(adopted.Live, adopted.RulesHash, now);
        var expected = adopted.Live.CashThb - 50m;
        Check(TradingCashBalance.SettledCash(ledger) == expected, "Only post-baseline rounding is counted.");
        Check(TradingCashBalance.Matches(ledger, ledger.CashThb), "Exact cash remains accepted.");
        Check(!TradingCashBalance.Matches(ledger, expected - 0.001m), "Unexplained sub-satang shortage is rejected.");
        Check(!TradingCashBalance.Matches(ledger, -0.01m), "Negative cash is rejected.");
        Check(TradingCashBalance.SettledCash(ledger with { CashBaselines = [] }) is null, "A baseline is required.");
        Check(
            TradingCashBalance.SettledCash(ledger with { FundingThb = ledger.FundingThb + 1 }) is null,
            "Unexplained funding changes are not rounding."
        );
        Check(
            TradingCashBalance.SettledCash(ledger with { CashThb = ledger.CashThb + 0.001m }) is null,
            "Raw cash must replay exactly before deriving settlement."
        );
        var last = ledger.Fills[^1];
        Check(
            TradingCashBalance.SettledCash(
                ledger with
                {
                    Fills = ledger
                        .Fills.Append(last with { Id = Guid.NewGuid(), BrokerFillId = "another-fill" })
                        .ToArray(),
                }
            )
                is null,
            "Multiple executions in one order have unsupported rounding granularity."
        );
        Check(
            TradingCashBalance.SettledCash(
                ledger with
                {
                    Fills = ledger.Fills.SkipLast(1).Append(last with { GrossFeeThb = null }).ToArray(),
                }
            )
                is null,
            "Unknown fee evidence is not rounded."
        );
        var twice = WithExecutions(ledger, adopted.RulesHash, now.AddMinutes(5));
        Check(
            TradingCashBalance.SettledCash(twice) == expected - 50m && twice.CashThb - (expected - 50m) == 0.018m,
            "Several independently verified order residuals are derived, not capped by an arbitrary epsilon."
        );
        BuySettlement(adopted.Live, adopted.RulesHash, now);
        Console.WriteLine(
            "Trading cash precision: verified single-execution settlement, retained history, exact mismatch refusal and no broker writes."
        );
    }

    private static void BuySettlement(TradingLedger baseline, string hash, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        var fill = new TradingFill(
            Guid.NewGuid(),
            id,
            "fractional-buy",
            "ETH",
            TradeSide.Buy,
            1m,
            100.004m,
            100.004m,
            1m,
            now.AddMinutes(-1),
            GrossFeeThb: 1.51m,
            FeeCreditThb: 0.51m
        )
        {
            QuantityScale = 8,
        };
        var order = new TradingIntent(
            id,
            "ETH",
            TradeSide.Buy,
            200m,
            100.004m,
            99.996m,
            now.AddMinutes(-2),
            1,
            hash,
            "synthetic partial buy",
            "cancelled",
            "cancelled-buy",
            CancellationState: "confirmed"
        );
        var ledger = TradingLedgerAccounting.Apply(baseline, fill);
        ledger = ledger with { Orders = ledger.Orders.Append(order).ToArray() };
        var expected = baseline.CashThb - 101.01m;
        Check(
            TradingCashBalance.SettledCash(ledger) == expected,
            "Buy debit including known net fees rounds up, including a single fill in a confirmed cancelled order."
        );
        Check(TradingCashBalance.Matches(ledger, expected), "Confirmed cancellation preserves one settled execution.");
        Check(
            !TradingCashBalance.Matches(ledger, expected - 0.01m),
            "Buy rounding never masks another satang shortage."
        );
        foreach (var cancellation in new[] { "claimed", "unknown" })
        {
            Check(
                TradingCashBalance.SettledCash(
                    ledger with
                    {
                        Orders = ledger
                            .Orders.SkipLast(1)
                            .Append(order with { CancellationState = cancellation })
                            .ToArray(),
                    }
                )
                    is null,
                "Uncertain cancellation cannot support settlement proof."
            );
        }
    }

    private static async Task Activation(
        TradingState adopted,
        string account,
        bool aiManaged,
        decimal cashDifference,
        bool accepted
    )
    {
        using var fixture = new AutonomousFixture(account);
        fixture.Time.Now = adopted.Live.CashBaselines[^1].Cutoff.AddMinutes(10);
        var rules = fixture.Rules with { Version = "adaptive-intraday-v2" };
        var hash = TradingRuleHash.Compute(rules);
        var ledger = WithExecutions(adopted.Live, hash, fixture.Time.Now);
        TradingCashBaselineAccounting.Validate(ledger, account);
        TradingManualSaleInventory.Validate(ledger, account);
        fixture.Store.State = adopted with { Rules = rules, RulesHash = hash, Live = ledger };
        fixture.Markets.Books = [fixture.Book("ETH")];
        fixture.Markets.Universe = new(fixture.Time.Now, ["ETH"], 1, 0, null);
        var cash = adopted.Live.CashThb - 50m + cashDifference;
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with
        {
            ObservedAt = fixture.Time.Now,
            Balances = [new Balance("THB", cash, cash), new Balance("ETH", 0.5m, 0.5m)],
        };
        var before = fixture.Store.State;
        var activated = false;
        try
        {
            await fixture.Control.Activate(
                account,
                hash,
                fixture.Time.Now.AddHours(1),
                aiManaged ? "enable-ai-managed-trading" : "enable-auto-trading",
                CancellationToken.None,
                aiManaged
            );
            activated = true;
        }
        catch (ArgumentException) when (!accepted) { }

        var after = fixture.Store.State;
        Check(activated == accepted, "Activation must accept only an exact explained broker settlement.");
        Check(
            ReferenceEquals(after.Live, ledger),
            "Activation retains cash, funding, history, holdings and baseline byte-for-byte."
        );
        Check(after.RulesHash == hash && after.Rules == rules, "Activation preserves strategy rules.");
        Check(
            fixture.Exchange.Submissions == 0 && fixture.Exchange.Cancellations == 0,
            "Activation never submits broker orders."
        );
        Check(
            accepted ? after.Mode == TradingMode.AutoLive && after.Mandate is { RevokedAt: null } : after == before,
            "A refused activation preserves the stopped authority."
        );
        if (accepted && aiManaged)
        {
            Check(TradingPolicyAuthority.Matches(after), "AI-managed v2 retains valid mandate lineage.");
        }
    }

    private static TradingLedger WithExecutions(TradingLedger ledger, string hash, DateTimeOffset now)
    {
        var buyId = Guid.NewGuid();
        var sellId = Guid.NewGuid();
        var buy = new TradingFill(
            Guid.NewGuid(),
            buyId,
            buyId.ToString("N"),
            "ETH",
            TradeSide.Buy,
            1m,
            100m,
            100m,
            0m,
            now.AddMinutes(-2),
            GrossFeeThb: 0m,
            FeeCreditThb: 0m
        )
        {
            QuantityScale = 8,
        };
        var sell = new TradingFill(
            Guid.NewGuid(),
            sellId,
            sellId.ToString("N"),
            "ETH",
            TradeSide.Sell,
            0.5m,
            100.018m,
            50.009m,
            0m,
            now.AddMinutes(-1),
            GrossFeeThb: 0m,
            FeeCreditThb: 0m
        )
        {
            QuantityScale = 8,
        };
        var updated = TradingLedgerAccounting.Apply(TradingLedgerAccounting.Apply(ledger, buy), sell);
        return updated with
        {
            Orders = updated
                .Orders.Concat([
                    new TradingIntent(
                        buyId,
                        "ETH",
                        TradeSide.Buy,
                        100m,
                        100m,
                        0m,
                        now.AddMinutes(-3),
                        1,
                        hash,
                        "synthetic cash regression",
                        "filled",
                        buyId.ToString("N")
                    ),
                    new TradingIntent(
                        sellId,
                        "ETH",
                        TradeSide.Sell,
                        0.5m,
                        100.018m,
                        0m,
                        now.AddMinutes(-2),
                        2,
                        hash,
                        "synthetic cash regression",
                        "filled",
                        sellId.ToString("N")
                    ),
                ])
                .ToArray(),
        };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
