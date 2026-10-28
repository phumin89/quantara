using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class CashBaselineActivationJourneyChecks
{
    public static async Task Run(TradingState retired, string account, DateTimeOffset soldAt)
    {
        foreach (var uncertain in new[] { false, true })
        {
            await Verify(retired, account, soldAt, uncertain);
        }

        Console.WriteLine(
            "Cash recovery journey: reviewed cash, explicit v2 mandate, durable first fake send and delayed restart without retry."
        );
    }

    private static async Task Verify(TradingState retired, string account, DateTimeOffset soldAt, bool uncertain)
    {
        using var fixture = new AutonomousFixture(account);
        fixture.Time.Now = soldAt.AddDays(1);
        retired = retired with
        {
            Mandate = new TradingMandate(
                Guid.NewGuid(),
                account,
                retired.RulesHash,
                soldAt.AddHours(-1),
                soldAt.AddHours(1),
                soldAt
            ),
        };
        fixture.Store.State = retired;
        fixture.Markets.Books = [fixture.Book("BTC"), fixture.Book("ETH")];
        fixture.Markets.Universe = new(fixture.Time.Now, ["ETH"], 1, 0, null);
        fixture.Exchange.AvailableCash = 5096.69m;
        fixture.Exchange.AvailableBase = 0m;
        fixture.Exchange.FailSubmission = uncertain;
        fixture.Evidence.Evidence = new(
            fixture.Time.Now,
            [new Balance("THB", 5096.69m, 5096.69m), new Balance("WLD", 0m, 0m)],
            [],
            [],
            soldAt.AddDays(-90)
        );
        Check(
            retired.Mode == TradingMode.Stopped
                && retired.Live.ExternalSales.Single().Settlement is null
                && retired.Live.CashBaselines.Count == 0,
            "The journey starts with genuinely reviewed inventory and unresolved historical cash."
        );
        var workflow = new CashBaselineReviewService(
            fixture.Account,
            fixture.Store,
            new FakeOrderStore(),
            fixture.Evidence,
            new ManualSaleReviewProof(new TradingControlOptions("owner-token")),
            fixture.Time,
            fixture.Gate
        );
        var preview = await workflow.Preview(CancellationToken.None);
        Check(
            fixture.Store.State == retired && fixture.Exchange.Submissions == 0,
            "Preview changes neither retained state nor broker orders."
        );
        var applied = await workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None);
        var adopted = fixture.Store.State;
        Check(
            applied.Applied
                && adopted.Mode == TradingMode.Stopped
                && adopted.Mandate == retired.Mandate
                && adopted.Live.CashThb == 5096.69m
                && adopted.Live.FundingThb == retired.Live.FundingThb
                && adopted.Live.CashBaselines.Count == 1
                && adopted.Live.ExternalSales.SequenceEqual(retired.Live.ExternalSales)
                && adopted.Live.Fills.SequenceEqual(retired.Live.Fills),
            "Cash adoption preserves history and never grants live authority."
        );
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Store.State.Mode == TradingMode.Stopped && fixture.Exchange.Submissions == 0,
            "An eligible market cannot trade after cash review alone."
        );
        var beforePreparation = fixture.Store.State;
        var prepared = await fixture.Control.PrepareAdaptive(account, adopted.RulesHash, CancellationToken.None);
        Check(
            prepared.Mode == TradingMode.Stopped
                && prepared.Rules.Version == "adaptive-intraday-v2"
                && prepared.Live == beforePreparation.Live
                && fixture.Exchange.Submissions == 0,
            "Preparing the reviewed v2 policy retains the adopted cash and inventory history."
        );
        var expiresAt = fixture.Time.Now.AddHours(1);
        var enabled = await fixture.Control.Activate(
            account,
            prepared.RulesHash,
            expiresAt,
            "enable-auto-trading",
            CancellationToken.None
        );
        Check(
            enabled.Mode == TradingMode.AutoLive
                && enabled.Mandate is { RevokedAt: null } mandate
                && mandate.AccountKey == account
                && mandate.RulesHash == prepared.RulesHash
                && mandate.ExpiresAt == expiresAt
                && enabled.Live == prepared.Live
                && fixture.Exchange.Submissions == 0,
            "Only the exact explicit owner confirmation grants the new mandate; enabling does not submit."
        );
        var claimed = 0;
        fixture.Exchange.BeforeSend = (intent, cancellation) =>
        {
            Check(
                !cancellation
                    && fixture.Store.State.Mode == TradingMode.AutoLive
                    && fixture.Store.State.Live.Orders.Single(order => order.Id == intent.Id).Status == "submitting"
                    && intent.Symbol == "ETH"
                    && intent.RulesHash == prepared.RulesHash,
                "The first fake broker send follows its exact durable claim and reviewed policy."
            );
            claimed++;
        };
        await fixture.Restart().Tick(CancellationToken.None);
        var first = fixture.Store.State.Live.Orders.Single();
        Check(
            claimed == 1 && fixture.Exchange.Submissions == 1 && first.Status == (uncertain ? "unknown" : "submitted"),
            "The recovered account reaches exactly one fake submission with an explicit outcome."
        );
        if (!uncertain)
        {
            var brokerId = first.BrokerOrderId ?? throw new InvalidOperationException("Missing fake broker receipt.");
            var available = adopted.Live.CashThb - first.Amount * (1 + prepared.Rules.FeeBps / 10000m);
            fixture.Exchange.AvailableCash = available;
            fixture.Evidence.Evidence = fixture.Evidence.Evidence with
            {
                Balances = [new Balance("THB", adopted.Live.CashThb, available), new Balance("WLD", 0m, 0m)],
                OpenOrders = [new(brokerId, first.Symbol, first.Side, first.Remaining, first.LimitPrice)],
            };
        }

        var inspections = 0;
        fixture.Exchange.Observation = intent =>
        {
            inspections++;
            return new(uncertain ? "unknown" : "submitted", intent.BrokerOrderId, intent.Remaining, []);
        };
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        await fixture.Restart().Tick(CancellationToken.None);
        var retained = fixture.Store.State.Live;
        Check(
            inspections == 1
                && claimed == 1
                && fixture.Exchange.Submissions == 1
                && fixture.Exchange.Cancellations == 0
                && retained.Orders.Single().Id == first.Id
                && retained.Orders.Single().Status == first.Status
                && retained.Orders.Single().BrokerOrderId == first.BrokerOrderId
                && (uncertain || fixture.Store.State.Fault is null)
                && retained.CashThb == adopted.Live.CashThb
                && retained.FundingThb == adopted.Live.FundingThb
                && retained.CashBaselines.SequenceEqual(adopted.Live.CashBaselines)
                && retained.ExternalSales.SequenceEqual(adopted.Live.ExternalSales)
                && retained.Fills.SequenceEqual(adopted.Live.Fills),
            "Restart never resends the order or rewrites cash, funding, settlement or retained fills."
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
