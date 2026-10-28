using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingSubmissionEvidenceChecks
{
    public static async Task Run()
    {
        await ExpiresDuringPreparation(90, 0, 59, 2, "signal");
        await ExpiresDuringPreparation(10, 8, 0, 3, "capital");
        await FreshAtBoundary();
        await UsesRefreshedSizing();
        await SmallerSizingBeforeClaim();
        await LostResizeClaim();
        await LiquidityWaitThenQuote();
        await UnknownAmountRetained();
        Console.WriteLine("Final trading submission evidence checks passed.");
    }

    private static async Task ExpiresDuringPreparation(
        int maximumDataAgeSeconds,
        int capitalAgeSeconds,
        int signalAgeSeconds,
        int preparationSeconds,
        string expiredEvidence
    )
    {
        using var fixture = await CreateFixture(maximumDataAgeSeconds, capitalAgeSeconds, signalAgeSeconds);
        var retained = fixture.Store.State.Live.Orders.Single();
        var preparations = 0;
        fixture.Exchange.BeforePrepare = () =>
        {
            preparations++;
            CheckCurrentSizing(fixture, retained);
            fixture.Time.Now = fixture.Time.Now.AddSeconds(preparationSeconds);
            Check(
                fixture.Time.Now - fixture.Markets.Books.Single().ObservedAt
                    <= TimeSpan.FromSeconds(maximumDataAgeSeconds),
                "The fixture quote must stay fresh while only " + expiredEvidence + " expires."
            );
        };

        await fixture.Engine.Tick(CancellationToken.None);
        var cancelled = fixture.Store.State.Live.Orders.Single(order => order.Id == retained.Id);
        Check(
            preparations == 1,
            "Accepted sizing must reach broker preparation before " + expiredEvidence + " expires."
        );
        Check(
            fixture.Store.History.Any(state =>
                state.Live.Orders.Any(order => order.Id == retained.Id && order.Status == "submitting")
            ),
            "The final evidence guard must be exercised after its durable claim."
        );
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0
                && cancelled.Status == "cancelled"
                && cancelled.BrokerOrderId is null
                && cancelled.Amount == retained.Amount
                && cancelled.LimitPrice == retained.LimitPrice
                && cancelled.Reason == retained.Reason
                && cancelled.LastExecutionMessage == "Not submitted: final trading checks no longer passed.",
            "Expired " + expiredEvidence + " must cancel only the known-unsent exact intent without a broker call."
        );

        await fixture.Restart().Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && preparations == 1
                && fixture.Store.State.Live.Orders.Single(order => order.Id == retained.Id).Status == "cancelled",
            "Restart must not resend an intent rejected by the final evidence guard."
        );
    }

    private static async Task FreshAtBoundary()
    {
        using var fixture = await CreateFixture(90, 0, 59);
        var retained = fixture.Store.State.Live.Orders.Single();
        var preparations = 0;
        fixture.Exchange.BeforePrepare = () =>
        {
            preparations++;
            CheckCurrentSizing(fixture, retained);
            fixture.Time.Now = fixture.Time.Now.AddSeconds(1);
        };
        fixture.Exchange.BeforeSend = (intent, cancellation) =>
            CheckExactClaim(fixture, retained, intent, cancellation);

        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            preparations == 1
                && fixture.Exchange.Submissions == 1
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single(order => order.Id == retained.Id).Status == "submitted",
            "Signal evidence exactly at its inclusive age limit must submit the original intent once."
        );
    }

    private static async Task UsesRefreshedSizing()
    {
        using var fixture = await CreateFixture(10, 0, 0);
        var retained = fixture.Store.State.Live.Orders.Single();
        var originalSizing = retained.Sizing ?? throw new InvalidOperationException("Fixture sizing is missing.");
        fixture.Time.Now = fixture.Time.Now.AddSeconds(20);
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with { ObservedAt = fixture.Time.Now };
        fixture.Markets.Books = [fixture.Book("ETH")];
        fixture.Markets.Universe = new(fixture.Time.Now, ["ETH"], 1, 0, null);
        var preparations = 0;
        fixture.Exchange.BeforePrepare = () =>
        {
            preparations++;
            CheckCurrentSizing(fixture, retained);
            var current = fixture.Audit.Decisions.Last();
            Check(
                fixture.Time.Now - originalSizing.Capital.ObservedAt > TimeSpan.FromSeconds(10)
                    && current.Capital.ObservedAt == fixture.Time.Now
                    && current.Market.ObservedAt == fixture.Time.Now,
                "The original capital is stale while the current execution sizing has refreshed capital and quote."
            );
        };
        fixture.Exchange.BeforeSend = (intent, cancellation) =>
            CheckExactClaim(fixture, retained, intent, cancellation);

        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            preparations == 1
                && fixture.Exchange.Submissions == 1
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single(order => order.Id == retained.Id).Status == "submitted",
            "Final submission must use refreshed sizing rather than reject a stale retained snapshot."
        );
    }

    private static async Task<AutonomousFixture> CreateFixture(
        int maximumDataAgeSeconds,
        int capitalAgeSeconds,
        int signalAgeSeconds
    )
    {
        var fixture = new AutonomousFixture();
        try
        {
            var policy = fixture.Rules.Adaptive ?? throw new InvalidOperationException("Fixture policy is missing.");
            var rules = fixture.Rules with
            {
                MaxDataAgeSeconds = maximumDataAgeSeconds,
                Adaptive = policy with
                {
                    Risk = policy.Risk with
                    {
                        MaximumDataAgeSeconds = maximumDataAgeSeconds,
                        MaximumSignalAgeSeconds = 60,
                    },
                },
            };
            fixture.Store.State = fixture.Store.State with
            {
                Rules = rules,
                RulesHash = TradingRuleHash.Compute(rules),
            };
            fixture.Evidence.Evidence = fixture.Evidence.Evidence with
            {
                ObservedAt = fixture.Time.Now.AddSeconds(-capitalAgeSeconds),
            };
            await fixture.Activate();

            var book = fixture.Markets.Books.Single();
            var id = Guid.NewGuid();
            var signal = new RiskSignal(
                99,
                book.Bars[^1].Timestamp,
                fixture.Time.Now.AddSeconds(-signalAgeSeconds),
                "Synthetic completed-candle submission evidence"
            );
            var sizing = await fixture.Risk.Size(id, rules, book, 110m, signal, false, CancellationToken.None);
            Check(
                sizing.Decision.Accepted && sizing.Decision.Amount is { } amount && amount >= 100m,
                "Fixture entry must pass sizing before its exact pending intent is installed."
            );
            var intent = new TradingIntent(
                id,
                "ETH",
                TradeSide.Buy,
                100m,
                110m,
                100m,
                fixture.Time.Now,
                book.Bars[^1].Timestamp,
                fixture.Store.State.RulesHash,
                "Synthetic final evidence guard",
                "pending",
                Sizing: sizing
            );
            fixture.Store.State = fixture.Store.State with
            {
                Live = fixture.Store.State.Live with { Orders = [intent] },
            };
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    private static async Task SmallerSizingBeforeClaim()
    {
        using var fixture = await CreateFixture(90, 0, 0);
        var retained = MaximumPending(fixture);
        var seed = AdaptiveExitStrategy.Create(
            fixture.Store.State.Rules with
            {
                Version = "adaptive-intraday-v3",
                ExitPolicy = new(),
            },
            fixture.Markets.Books.Single(),
            new(TradingAction.Buy, retained.Reason, retained.Candle, 110m, EntryLimit: 110m, StopPrice: 99m),
            retained.Id,
            retained.RulesHash,
            fixture.Time.Now
        );
        retained = retained with { ExitStudyPlan = seed };
        fixture.Store.State = fixture.Store.State with { Live = fixture.Store.State.Live with { Orders = [retained] } };
        fixture.Exchange.BeforeSend = (sent, cancellation) =>
        {
            Check(
                !cancellation
                    && sent.Id != retained.Id
                    && sent.ReplacesIntentId == retained.Id
                    && sent.Amount > 0m
                    && sent.Amount < retained.Amount
                    && sent.OriginalAmount == retained.Amount
                    && sent.ExitStudyPlan == seed with { EntryIntentId = sent.Id }
                    && sent.Remaining == sent.Amount
                    && sent.LimitPrice == retained.LimitPrice
                    && sent.RulesHash == retained.RulesHash
                    && fixture.Store.State.Live.Orders.Single(order => order.Id == sent.Id).Amount == sent.Amount
                    && fixture.Store.State.Live.Orders.Single(order => order.Id == sent.Id).Status == "submitting"
                    && fixture.Store.State.Live.Orders.Single(order => order.Id == retained.Id).Amount
                        == retained.Amount
                    && fixture.Store.State.Live.Orders.Single(order => order.Id == retained.Id).Status == "cancelled",
                "A smaller permitted size must be persisted and claimed before sending once at the original limit."
            );
        };
        await fixture.Engine.Tick(CancellationToken.None);
        Check(fixture.Exchange.Submissions == 1, "A small capacity decrease must not discard the entire entry.");
        var sent = fixture.Store.State.Live.Orders.Single(order => order.ReplacesIntentId == retained.Id);
        await fixture.Restart().Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 1
                && fixture.Store.State.Live.Orders.Single(order => order.Id == sent.Id).Amount == sent.Amount,
            "Restart must not resize or resubmit an already submitted intent."
        );
    }

    private static async Task LostResizeClaim()
    {
        using var fixture = await CreateFixture(90, 0, 0);
        var retained = MaximumPending(fixture);
        var intercepted = false;
        fixture.Store.BeforeSave = candidate =>
        {
            if (candidate.Live.Orders.Any(order => order.ReplacesIntentId == retained.Id))
            {
                fixture.Store.BeforeSave = null;
                fixture.Store.State = fixture.Store.State with { Revision = fixture.Store.State.Revision + 1 };
                intercepted = true;
            }
        };
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            intercepted
                && fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single().Amount == retained.Amount,
            "A lost resize persistence claim must never prepare a different order or send it."
        );
    }

    private static TradingIntent MaximumPending(AutonomousFixture fixture)
    {
        var intent = fixture.Store.State.Live.Orders.Single();
        var amount = intent.Sizing?.Decision.Amount ?? throw new InvalidOperationException("Fixture size missing.");
        intent = intent with { Amount = amount, Remaining = amount };
        fixture.Store.State = fixture.Store.State with { Live = fixture.Store.State.Live with { Orders = [intent] } };
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with { Balances = [new("THB", 4999.77m, 4999.77m)] };
        fixture.Risk.Invalidate();
        return intent;
    }

    private static async Task LiquidityWaitThenQuote()
    {
        using var fixture = await CreateFixture(90, 0, 0);
        var retained = fixture.Store.State.Live.Orders.Single();
        fixture.Markets.Books = [fixture.Book("ETH") with { Ask = 110.1m }];
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single().Status == "pending"
                && fixture.Store.State.Live.Orders.Single().LimitPrice == retained.LimitPrice,
            "An unsubmitted entry waits for liquidity at its exact limit instead of chasing price or cancelling."
        );
        fixture.Markets.Books = [fixture.Book("ETH")];
        await fixture.Engine.Process(
            new(TradingEventKind.QuoteChanged, "ETH", 1, fixture.Time.Now, book: fixture.Book("ETH")),
            CancellationToken.None
        );
        Check(
            fixture.Exchange.Submissions == 1 && fixture.Store.State.Live.Orders.Single().Amount == retained.Amount,
            "A fresh quote can complete an existing candle-backed pending entry without creating a new signal."
        );
    }

    private static async Task UnknownAmountRetained()
    {
        using var fixture = await CreateFixture(90, 0, 0);
        var retained = MaximumPending(fixture) with { Status = "unknown" };
        fixture.Store.State = fixture.Store.State with { Live = fixture.Store.State.Live with { Orders = [retained] } };
        fixture.Exchange.Observation = order => new("unknown", order.BrokerOrderId, null, []);
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single().Amount == retained.Amount
                && fixture.Store.State.Live.Orders.Single().Status == "unknown",
            "Unknown submission outcomes never authorize resizing or another send."
        );
    }

    private static void CheckCurrentSizing(AutonomousFixture fixture, TradingIntent retained)
    {
        var original = retained.Sizing ?? throw new InvalidOperationException("Fixture sizing is missing.");
        var current = fixture.Audit.Decisions.Last();
        Check(
            current.Id != original.Id
                && current.AccountKey == fixture.Account.AccountKey
                && current.Capital.AccountKey == fixture.Account.AccountKey
                && current.PolicyHash == fixture.Store.State.RulesHash
                && current.Decision.Accepted
                && current.Decision.Amount is { } amount
                && amount >= retained.Amount,
            "Broker preparation must follow an accepted refreshed sizing for the exact account and rules."
        );
    }

    private static void CheckExactClaim(
        AutonomousFixture fixture,
        TradingIntent retained,
        TradingIntent sent,
        bool cancellation
    )
    {
        Check(
            !cancellation
                && sent.Id == retained.Id
                && sent.Symbol == retained.Symbol
                && sent.Side == retained.Side
                && sent.Amount == retained.Amount
                && sent.LimitPrice == retained.LimitPrice
                && fixture.Store.State.Live.Orders.Single(order => order.Id == retained.Id).Status == "submitting",
            "A positive fixture sends the exact retained amount and limit only after its durable claim."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
