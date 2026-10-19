using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingPreparationChecks
{
    public static async Task Run()
    {
        await LegacyUsesServerDefaults();
        await UpgradePreservesReviewedLimits();
        await CurrentPoliciesAreUnchanged();
        await RejectInvalidReview();
        await PreserveConfigurationBlockers();
        await ReviewIdentityInsideGate();
        Console.WriteLine(
            "Trading preparation checks passed: defaults, retained limits, exact review identity, idempotency and no broker effects."
        );
    }

    private static async Task LegacyUsesServerDefaults()
    {
        using var fixture = new AutonomousFixture();
        var legacy = new TradingRuleSet
        {
            Symbols = ["ETH"],
            AllocatedThb = 2500,
            PerOrderThb = 200,
            FeeBps = 30,
            MaxSpreadBps = 40,
            MaxDataAgeSeconds = 120,
            MaxPositions = 3,
            TakeProfitBps = 350,
        };
        SetRules(fixture, legacy);
        var before = fixture.Store.State with { Mode = TradingMode.Paper };
        fixture.Store.State = before;
        var prepared = await fixture.Control.PrepareAdaptive(
            fixture.Account.AccountKey,
            before.RulesHash,
            CancellationToken.None
        );
        var expected = new TradingRuleSet
        {
            Version = "adaptive-intraday-v2",
            Symbols = [],
            MaxPositions = 16,
            Adaptive = new(),
        };
        prepared.Rules.Validate();
        Check(
            prepared.RulesHash == TradingRuleHash.Compute(expected)
                && prepared.RulesHash == TradingRuleHash.Compute(prepared.Rules),
            "Initial adaptive preparation uses complete server defaults, including aligned risk limits."
        );
        Check(
            prepared.Mode == TradingMode.Paper
                && prepared.Mandate is null
                && prepared.Revision == before.Revision + 1
                && fixture.Store.History.Count == 2,
            "Initial preparation creates exactly one reviewable revision without activating a mandate."
        );
        Check(
            prepared.Paper == before.Paper && prepared.Live == before.Live,
            "Changing the policy never resizes or replaces retained account ledgers."
        );
        CheckNoBrokerEffects(fixture);
    }

    private static async Task UpgradePreservesReviewedLimits()
    {
        using var fixture = new AutonomousFixture();
        var rules = CustomizedRules(fixture);
        SetRules(fixture, rules);
        var before = WithMandate(fixture);
        var mandate = before.Mandate ?? throw new InvalidOperationException("The fixture must retain its mandate.");
        fixture.Store.State = before;
        var prepared = await fixture.Control.PrepareAdaptive(
            fixture.Account.AccountKey,
            before.RulesHash,
            CancellationToken.None
        );
        var expected = rules with { Version = "adaptive-intraday-v2" };
        Check(
            prepared.RulesHash == TradingRuleHash.Compute(expected)
                && prepared.RulesHash == TradingRuleHash.Compute(prepared.Rules)
                && prepared.RulesHash != before.RulesHash,
            "The v1 upgrade changes only the version, preserving exclusions, relative caps and technical settings."
        );
        Check(
            prepared.Mode == TradingMode.Stopped
                && prepared.Mandate == mandate with { RevokedAt = fixture.Time.Now }
                && prepared.Revision == before.Revision + 1,
            "A material upgrade revokes the exact retained mandate and disarms live mode before owner review."
        );
        Check(
            prepared.Paper == before.Paper
                && prepared.Live == before.Live
                && prepared.Decisions == before.Decisions
                && prepared.Reports == before.Reports
                && prepared.ObservedFundingFlows == before.ObservedFundingFlows,
            "Preparing an upgrade preserves capital, fills, decisions, reports and deposit provenance."
        );
        var historyCount = fixture.Store.History.Count;
        var repeated = await fixture.Control.PrepareAdaptive(
            fixture.Account.AccountKey,
            prepared.RulesHash,
            CancellationToken.None
        );
        Check(
            repeated == prepared && fixture.Store.History.Count == historyCount,
            "Repeated preparation with the current review creates no additional write or authority."
        );
        CheckNoBrokerEffects(fixture);
    }

    private static async Task CurrentPoliciesAreUnchanged()
    {
        foreach (var version in new[] { "adaptive-intraday-v2", "adaptive-intraday-v3" })
        {
            using var fixture = new AutonomousFixture();
            SetRules(
                fixture,
                CustomizedRules(fixture) with
                {
                    Version = version,
                    ExitPolicy = version == "adaptive-intraday-v3" ? new() : null,
                }
            );
            var before = WithMandate(fixture);
            before = before with
            {
                Live = before.Live with
                {
                    Positions = [new TradingHolding("ETH", 1, 100, fixture.Time.Now, 100)],
                    Orders = [Intent(fixture, "unknown")],
                },
            };
            fixture.Store.State = before;
            var historyCount = fixture.Store.History.Count;
            var prepared = await fixture.Control.PrepareAdaptive(
                fixture.Account.AccountKey,
                before.RulesHash,
                CancellationToken.None
            );
            Check(
                prepared == before && fixture.Store.History.Count == historyCount,
                "Existing " + version + " preparation never changes its hash, live authority, exposure or revision."
            );
            CheckNoBrokerEffects(fixture);
        }
    }

    private static async Task RejectInvalidReview()
    {
        using var fixture = new AutonomousFixture();
        foreach (var accountKey in new[] { "", " ", "another-account", new string('a', 129) })
        {
            await Refused(
                fixture,
                () =>
                    fixture.Control.PrepareAdaptive(accountKey, fixture.Store.State.RulesHash, CancellationToken.None),
                "Blank, oversized or mismatched account identity cannot prepare a policy."
            );
        }

        foreach (var rulesHash in new[] { "", " ", "wrong-hash", fixture.Store.State.RulesHash.ToUpperInvariant() })
        {
            await Refused(
                fixture,
                () => fixture.Control.PrepareAdaptive(fixture.Account.AccountKey, rulesHash, CancellationToken.None),
                "A malformed or noncanonical rules identity cannot prepare a policy."
            );
        }

        var staleHash = fixture.Store.State.RulesHash;
        SetRules(fixture, fixture.Rules with { PerOrderThb = 150 });
        await Refused(
            fixture,
            () => fixture.Control.PrepareAdaptive(fixture.Account.AccountKey, staleHash, CancellationToken.None),
            "A stale review hash cannot overwrite newer policy settings."
        );
        fixture.Store.State = fixture.Store.State with { RulesHash = new string('a', 64) };
        await Refused(
            fixture,
            () =>
                fixture.Control.PrepareAdaptive(
                    fixture.Account.AccountKey,
                    fixture.Store.State.RulesHash,
                    CancellationToken.None
                ),
            "An inconsistent stored rules hash fails closed even when the request repeats it."
        );
    }

    private static async Task PreserveConfigurationBlockers()
    {
        foreach (var live in new[] { false, true })
        {
            foreach (var status in new[] { "pending", "unknown" })
            {
                using var fixture = new AutonomousFixture();
                var before = WithMandate(fixture);
                var ledger = (live ? before.Live : before.Paper) with { Orders = [Intent(fixture, status)] };
                fixture.Store.State = live ? before with { Live = ledger } : before with { Paper = ledger };
                await Refused(
                    fixture,
                    () =>
                        fixture.Control.PrepareAdaptive(
                            fixture.Account.AccountKey,
                            fixture.Store.State.RulesHash,
                            CancellationToken.None
                        ),
                    "Preparation retains the configuration blocker for outstanding " + status + " orders."
                );
            }

            using var exposure = new AutonomousFixture();
            SetRules(exposure, exposure.Rules with { MaxPositions = 1 });
            var exposed = WithMandate(exposure);
            var positions = new[]
            {
                new TradingHolding("BTC", 1, 100, exposure.Time.Now, 100),
                new TradingHolding("ETH", 1, 100, exposure.Time.Now, 100),
            };
            var owned = (live ? exposed.Live : exposed.Paper) with { Positions = positions };
            exposure.Store.State = live ? exposed with { Live = owned } : exposed with { Paper = owned };
            await Refused(
                exposure,
                () =>
                    exposure.Control.PrepareAdaptive(
                        exposure.Account.AccountKey,
                        exposure.Store.State.RulesHash,
                        CancellationToken.None
                    ),
                "Preparation cannot bypass the retained maximum-position configuration blocker."
            );
        }
    }

    private static async Task ReviewIdentityInsideGate()
    {
        using var fixture = new AutonomousFixture();
        var reviewedHash = fixture.Store.State.RulesHash;
        await fixture.Gate.WaitAsync();
        var pending = fixture.Control.PrepareAdaptive(fixture.Account.AccountKey, reviewedHash, CancellationToken.None);
        Check(!pending.IsCompleted, "Preparation must wait for the shared trading gate.");
        SetRules(fixture, fixture.Rules with { PerOrderThb = 150 });
        var current = fixture.Store.State;
        fixture.Gate.Release();
        await Refused(
            fixture,
            () => pending,
            "The exact review identity is checked after acquiring the gate, preserving intervening changes."
        );
        Check(fixture.Store.State == current, "A delayed preparation never overwrites the current rules.");
    }

    private static TradingRuleSet CustomizedRules(AutonomousFixture fixture) =>
        fixture.Rules with
        {
            AllocatedThb = 2222,
            PerOrderThb = 111,
            DailyLossThb = 77,
            MaxSpreadBps = 40,
            SlippageBps = 18,
            TakeProfitBps = 234,
            StopLossBps = 123,
            TrailingStopBps = 89,
            FeeBps = 30,
            VolumeRatio = 1.7m,
            MaxPositions = 7,
            MaxDataAgeSeconds = 120,
            TimeExitMinutes = 187,
            CooldownMinutes = 13,
            OrderLifetimeMinutes = 19,
            Adaptive = new()
            {
                ExcludedSymbols = ["USDT", "BTC"],
                DailyLossFraction = 0.04m,
                DailyProfitTargetThb = 321,
                Risk = new()
                {
                    Profile = RiskProfile.Aggressive,
                    MaximumExposureFraction = 0.6m,
                    MaximumConcentrationFraction = 0.2m,
                    RiskToStopFraction = 0.02m,
                    AtrMultiplier = 1.5m,
                    MaximumSpreadFraction = 0.004m,
                    EstimatedFeeRate = 0.003m,
                    MaximumDataAgeSeconds = 120,
                    MaximumSignalAgeSeconds = 420,
                },
            },
        };

    private static void SetRules(AutonomousFixture fixture, TradingRuleSet rules)
    {
        rules.Validate();
        fixture.Store.State = fixture.Store.State with { Rules = rules, RulesHash = TradingRuleHash.Compute(rules) };
    }

    private static TradingState WithMandate(AutonomousFixture fixture) =>
        fixture.Store.State with
        {
            Mode = TradingMode.AutoLive,
            Mandate = new(
                Guid.NewGuid(),
                fixture.Account.AccountKey,
                fixture.Store.State.RulesHash,
                fixture.Time.Now.AddMinutes(-1),
                fixture.Time.Now.AddHours(1),
                null
            ),
        };

    private static TradingIntent Intent(AutonomousFixture fixture, string status) =>
        new(
            Guid.NewGuid(),
            "ETH",
            TradeSide.Buy,
            100,
            100,
            100,
            fixture.Time.Now,
            0,
            fixture.Store.State.RulesHash,
            "Preparation fixture",
            status
        );

    private static async Task Refused(AutonomousFixture fixture, Func<Task<TradingState>> operation, string message)
    {
        var before = fixture.Store.State;
        var historyCount = fixture.Store.History.Count;
        try
        {
            await operation();
        }
        catch (ArgumentException)
        {
            Check(fixture.Store.State == before && fixture.Store.History.Count == historyCount, message);
            CheckNoBrokerEffects(fixture);
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void CheckNoBrokerEffects(AutonomousFixture fixture) =>
        Check(
            fixture.Exchange.Submissions == 0 && fixture.Exchange.Cancellations == 0,
            "Preparation never submits or cancels a broker order."
        );

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
