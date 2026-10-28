using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingPendingExitChecks
{
    public static async Task Run()
    {
        await TradingCancellationRecoveryChecks.Run();
        await ExpiryBeforeClaim();
        await ExpiryDuringPreparation();
        await ExpiryDuringClaim();
        await StaleDuringPreparation();
        await StaleDuringClaim();
        await ChangedClaimStaysUncertain();
        await UnsentReceiptFailure(false);
        await UnsentReceiptFailure(true);
        await LostSubmissionClaim();
        await PersistedSubmissionClaimFailure();
        await AcceptedSubmissionReceiptFailure(false);
        await AcceptedSubmissionReceiptFailure(true);
        await PreflightDiagnostics();
        await ManualReduction(0m);
        await ManualReduction(.1m);
        await ManualReduction(.19998098m);
        await StoppedExternalReduction(0m);
        await StoppedExternalReduction(.1m);
        await StoppedExternalReduction(.19998098m);
        await ZeroWalletResidualQuarantined(8);
        await ZeroWalletResidualQuarantined(2);
        await ZeroWalletResidualQuarantined(8, 268.30338665m);
        await StoppedBrokerIdentityNeedsInspection(true);
        await StoppedBrokerIdentityNeedsInspection(false);
        await ReservedFunds();
        await UncertainPeer();
        await UncertainSubmission();
        await BrokerIdentityCannotResubmit();
        await InvalidRetainedProposal();
        Console.WriteLine(
            "Pending exits: expiry/final quote freshness, CAS/persistence failures, sanitized stages, manual reductions and residual quarantine, stopped restart, reserved funds, unknown outcomes and dedup passed with fake brokers only."
        );
    }

    private static async Task ExpiryBeforeClaim()
    {
        using var fixture = await Pending();
        fixture.Exchange.BeforeMarket = () => fixture.Time.Now = fixture.Time.Now.AddSeconds(2);
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0 && fixture.Store.State.Live.Orders.Single().Status == "cancelled",
            "An exit that expires during preflight must be cancelled locally before any durable submission claim or send."
        );
    }

    private static async Task ExpiryDuringClaim()
    {
        using var fixture = await Pending();
        var originalId = fixture.Store.State.Live.Orders.Single().Id;
        fixture.Exchange.BeforeSend = (intent, _) =>
            Check(intent.Id != originalId, "The expired intent must never reach the broker.");
        fixture.Store.BeforeSave = state =>
        {
            if (state.Live.Orders.Any(order => order.Status == "submitting"))
            {
                fixture.Time.Now = fixture.Time.Now.AddSeconds(2);
            }
        };
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single() is { Status: "cancelled", BrokerOrderId: null },
            "Expiry after a successful claim is recorded as unsent before any broker call."
        );
        fixture.Store.BeforeSave = null;
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Store.State.Live.Orders.Single(order => order.Id == originalId).Status == "cancelled",
            "Later decisions retain the unsent receipt instead of reopening the expired intent."
        );
    }

    private static async Task ExpiryDuringPreparation()
    {
        using var fixture = await Pending();
        fixture.Exchange.BeforePrepare = () => fixture.Time.Now = fixture.Time.Now.AddSeconds(2);
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0 && fixture.Store.State.Live.Orders.Single().Status == "cancelled",
            "Expiry while preparing the broker request still cancels locally before the durable claim."
        );
    }

    private static async Task LostSubmissionClaim()
    {
        using var fixture = await Pending(false);
        var original = fixture.Store.State.Live.Orders.Single();
        var owned = fixture.Store.State.Live.Positions.Single();
        var lost = false;
        fixture.Store.BeforeSave = candidate =>
        {
            if (candidate.Live.Orders.Single().Status == "submitting")
            {
                fixture.Store.BeforeSave = null;
                fixture.Store.State = fixture.Store.State with { Revision = fixture.Store.State.Revision + 1 };
                fixture.Store.History.Add(fixture.Store.State);
                lost = true;
            }
        };
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            lost
                && fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single() == original
                && fixture.Store.State.Live.Positions.Single() == owned
                && fixture.Store.State.Live.Fills.Count == 0
                && fixture.Store.State.Fault is not null
                && !fixture.Store.History.Any(state => state.Live.Orders.Any(order => order.Status == "submitting")),
            "Losing the submission CAS to a concurrent revision never persists a claim or sends an order; the pending exit and accounting remain intact."
        );
    }

    private static async Task StaleDuringPreparation()
    {
        using var fixture = await Pending(false);
        var original = fixture.Store.State.Live.Orders.Single();
        fixture.Exchange.BeforePrepare = () =>
            fixture.Time.Now = fixture.Time.Now.AddSeconds(fixture.Store.State.Rules.MaxDataAgeSeconds + 1);
        await fixture.Engine.Tick(CancellationToken.None);
        var waiting = fixture.Store.State.Live.Orders.Single();
        Check(
            fixture.Exchange.Submissions == 0
                && waiting with { LastExecutionMessage = null, LastExecutionAt = null } == original
                && waiting.LastExecutionMessage
                    == "Waiting for a fresh quote after order preparation; no order has been sent.",
            "A quote that becomes stale during preparation cannot submit or claim an otherwise unexpired pending exit."
        );
    }

    private static async Task StaleDuringClaim()
    {
        using var fixture = await Pending(false);
        var retainedReason = fixture.Store.State.Live.Orders.Single().Reason;
        fixture.Store.BeforeSave = candidate =>
        {
            if (candidate.Live.Orders.Single().Status == "submitting")
            {
                fixture.Store.BeforeSave = null;
                fixture.Time.Now = fixture.Time.Now.AddSeconds(candidate.Rules.MaxDataAgeSeconds + 1);
            }
        };
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single() is { Status: "cancelled", BrokerOrderId: null }
                && fixture.Store.State.Live.Orders.Single().Reason == retainedReason
                && fixture.Store.State.Live.Orders.Single().LastExecutionMessage
                    == "Not submitted: final trading checks no longer passed.",
            "A stale final quote closes this call's known-unsent claim without inventing a broker result."
        );
        var originalId = fixture.Store.State.Live.Orders.Single().Id;
        fixture.Exchange.Observation = order => new("unknown", order.BrokerOrderId, null, []);
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        await fixture.Restart().Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single(order => order.Id == originalId).Status == "cancelled"
                && !fixture.Store.State.Live.Orders.Any(order => order.Status is "submitting" or "unknown")
                && fixture.Store.State.Live.Fills.Count == 0,
            "Restart preserves the unsent receipt without replaying the old intent or inventing uncertainty."
        );
    }

    private static async Task ChangedClaimStaysUncertain()
    {
        using var fixture = await Pending(false);
        fixture.Store.AfterSave = candidate =>
        {
            if (candidate.Live.Orders.Single().Status == "submitting")
            {
                fixture.Store.AfterSave = null;
                fixture.Store.State = candidate with { Revision = candidate.Revision + 1 };
                fixture.Time.Now = fixture.Time.Now.AddSeconds(candidate.Rules.MaxDataAgeSeconds + 1);
            }
        };
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0 && fixture.Store.State.Live.Orders.Single().Status == "submitting",
            "A changed revision cannot be resolved using this call's unsent proof."
        );
    }

    private static async Task UnsentReceiptFailure(bool persisted)
    {
        using var fixture = await Pending(false);
        var failed = false;
        fixture.Store.BeforeSave = candidate =>
        {
            if (candidate.Live.Orders.Single().Status == "submitting")
            {
                fixture.Time.Now = fixture.Time.Now.AddSeconds(candidate.Rules.MaxDataAgeSeconds + 1);
            }
        };
        Action<TradingState> failReceipt = candidate =>
        {
            if (candidate.Live.Orders.Single().Status == "cancelled")
            {
                fixture.Store.BeforeSave = null;
                fixture.Store.AfterSave = null;
                failed = true;
                throw new IOException("Fake unsent receipt persistence failure.");
            }
        };
        if (persisted)
        {
            fixture.Store.AfterSave = failReceipt;
        }
        else
        {
            var staleDuringClaim = fixture.Store.BeforeSave;
            fixture.Store.BeforeSave = candidate =>
            {
                staleDuringClaim?.Invoke(candidate);
                failReceipt(candidate);
            };
        }

        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            failed
                && fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single().Status == (persisted ? "cancelled" : "submitting")
                && fixture.Store.State.Live.Fills.Count == 0,
            "A failed unsent receipt retains the last durable state and never authorizes a broker call."
        );
    }

    private static async Task PersistedSubmissionClaimFailure()
    {
        using var fixture = await Pending(false);
        var original = fixture.Store.State.Live.Orders.Single();
        var owned = fixture.Store.State.Live.Positions.Single();
        var failed = false;
        fixture.Store.AfterSave = candidate =>
        {
            if (candidate.Live.Orders.Single().Status == "submitting")
            {
                fixture.Store.AfterSave = null;
                failed = true;
                throw new IOException("Fake durable claim acknowledgement failure.");
            }
        };
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            failed
                && fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single().Status == "submitting"
                && fixture.Store.State.Fault is not null,
            "A claim persisted before its save throws remains durable and prevents the original broker send."
        );
        fixture.Exchange.Observation = order => new("unknown", order.BrokerOrderId, null, []);
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        fixture.Markets.Books = [fixture.Book("ETH") with { Bid = 108m, Ask = 108.01m }];
        await fixture.Restart().Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single() is { Status: "unknown", BrokerOrderId: null } retained
                && retained.Id == original.Id
                && fixture.Store.State.Live.Positions.Single() == owned
                && fixture.Store.State.Live.Fills.Count == 0,
            "Restart reconciles an ambiguously acknowledged durable claim without replay, invented broker identity or fabricated fills."
        );
    }

    private static async Task AcceptedSubmissionReceiptFailure(bool persisted)
    {
        using var fixture = await Pending(false);
        var original = fixture.Store.State.Live.Orders.Single();
        var owned = fixture.Store.State.Live.Positions.Single();
        var failed = false;
        Action<TradingState> failReceipt = candidate =>
        {
            if (candidate.Live.Orders.Single().Status == "submitted")
            {
                fixture.Store.BeforeSave = null;
                fixture.Store.AfterSave = null;
                failed = true;
                throw new IOException("Fake accepted submission receipt persistence failure.");
            }
        };
        if (persisted)
        {
            fixture.Store.AfterSave = failReceipt;
        }
        else
        {
            fixture.Store.BeforeSave = failReceipt;
        }

        fixture.Exchange.BeforeSend = (intent, cancellation) =>
            Check(
                !cancellation
                    && fixture.Store.State.Live.Orders.Single(order => order.Id == intent.Id).Status == "submitting",
                "Even a receipt-write failure must have a durable submission claim before the fake broker accepts."
            );
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            failed
                && fixture.Exchange.Submissions == 1
                && fixture.Store.State.Live.Orders.Single().Status == (persisted ? "submitted" : "submitting")
                && fixture.Store.State.Fault is not null,
            "An accepted send with a failed receipt save preserves the last durable submission state without a second send."
        );
        fixture.Exchange.Observation = order => new("unknown", order.BrokerOrderId, null, []);
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        fixture.Markets.Books = [fixture.Book("ETH") with { Bid = 108m, Ask = 108.01m }];
        await fixture.Restart().Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 1
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single().Status == "unknown"
                && fixture.Store.State.Live.Orders.Single().Id == original.Id
                && fixture.Store.State.Live.Positions.Single() == owned
                && fixture.Store.State.Live.Fills.Count == 0,
            "Restart never repeats an accepted send whose receipt failed before or after persistence, and never invents a fill."
        );
    }

    private static async Task BrokerIdentityCannotResubmit()
    {
        using var fixture = await Pending();
        var order = fixture.Store.State.Live.Orders.Single() with { BrokerOrderId = "fake-existing-broker-identity" };
        fixture.Store.State = fixture.Store.State with { Live = fixture.Store.State.Live with { Orders = [order] } };
        fixture.Time.Now = fixture.Time.Now.AddSeconds(2);
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single() == order
                && fixture.Store.State.Fault is not null,
            "An ambiguous pending record with broker identity is neither replayed nor locally cancelled as unsent."
        );
    }

    private static async Task InvalidRetainedProposal()
    {
        using var fixture = await Pending(false);
        var order = fixture.Store.State.Live.Orders.Single() with { LimitPrice = 108.001m };
        fixture.Store.State = fixture.Store.State with { Live = fixture.Store.State.Live with { Orders = [order] } };
        var logger = new FakeTradingLogger();
        await Engine(fixture, logger).Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single() == order
                && logger.Messages.Single().Contains("ProposalValidation", StringComparison.Ordinal),
            "Current broker precision rejects an incompatible retained proposal before claim and records the safe guard stage."
        );
    }

    private static async Task PreflightDiagnostics()
    {
        using var fixture = await Pending(false);
        var logger = new FakeTradingLogger();
        var engine = Engine(fixture, logger);
        fixture.Exchange.BeforeMarket = () =>
            throw new HttpRequestException(
                "secret-account-and-token-do-not-log",
                null,
                System.Net.HttpStatusCode.ServiceUnavailable
            );
        await engine.Tick(CancellationToken.None);
        await engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Count == 1
                && fixture.Store.State.Live.Orders.Single().Status == "pending",
            "Repeated pre-claim failures retain one unchanged pending exit without submission or duplicate intents."
        );
        Check(
            logger.Messages.Count == 2
                && logger.Messages.All(message =>
                    message.Contains("BrokerMarket", StringComparison.Ordinal)
                    && message.Contains("HttpRequestException", StringComparison.Ordinal)
                    && message.Contains("503", StringComparison.Ordinal)
                    && !message.Contains("secret-account-and-token", StringComparison.Ordinal)
                )
                && !logger.ExceptionPayloadReceived,
            "Diagnostics retain stage/type/status without exception message, stack, secret data or raw exception payload."
        );
        fixture.Exchange.BeforeMarket = null;
        fixture.Exchange.BeforeBalances = () => throw new InvalidDataException("secret-balance-response");
        await engine.Tick(CancellationToken.None);
        Check(
            logger.Messages[^1].Contains("BrokerBalance", StringComparison.Ordinal)
                && !logger.Messages[^1].Contains("secret-balance-response", StringComparison.Ordinal),
            "Balance preflight failures have a safe distinct stage."
        );
        fixture.Exchange.BeforeBalances = null;
        fixture.Exchange.BeforePrepare = () => throw new InvalidOperationException("secret-signature-response");
        await engine.Tick(CancellationToken.None);
        Check(
            logger.Messages[^1].Contains("SubmissionPreparation", StringComparison.Ordinal)
                && !logger.Messages[^1].Contains("secret-signature-response", StringComparison.Ordinal),
            "Preparation failures retain one pending intent and a safe stage."
        );
    }

    private static async Task ManualReduction(decimal remaining)
    {
        using var fixture = await Pending(false);
        var original = fixture.Store.State.Live.Orders.Single();
        var owned = fixture.Store.State.Live.Positions.Single();
        fixture.Exchange.TotalBase = remaining;
        fixture.Exchange.AvailableBase = remaining;
        await fixture.Engine.Tick(CancellationToken.None);
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single(order => order.Id == original.Id).Status == "cancelled"
                && fixture.Store.State.Live.Positions.Single() == owned
                && fixture.Store.State.Live.Fills.Count == 0
                && fixture.Store.State.Fault is not null,
            "A fresh full/partial external ownership reduction invalidates only the local unsubmitted exit; ownership, cost and fills await reconciliation and are never invented or oversold."
        );
    }

    private static async Task StoppedExternalReduction(decimal remaining)
    {
        using var fixture = await Pending(false);
        var owned = fixture.Store.State.Live.Positions.Single();
        var mandate = fixture.Store.State.Mandate ?? throw new InvalidOperationException("Fixture mandate is missing.");
        fixture.Exchange.TotalBase = remaining;
        fixture.Exchange.AvailableBase = remaining;
        fixture.Exchange.BeforeBalances = () =>
            throw new InvalidOperationException("Stopped execution must not request submission preflight.");
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with
        {
            Balances = [new("THB", 5000m, 5000m), new("ETH", remaining, remaining)],
        };
        fixture.Store.State = fixture.Store.State with
        {
            Mode = TradingMode.Stopped,
            Mandate = mandate with { RevokedAt = fixture.Time.Now },
        };

        await fixture.Engine.Tick(CancellationToken.None);
        await fixture.Restart().Tick(CancellationToken.None);

        var state = fixture.Store.State;
        Check(
            state.Mode == TradingMode.Stopped
                && state.Mandate?.RevokedAt is not null
                && state.Live.Orders.Single().Status == "cancelled"
                && state.Live.Positions.Single().Quantity == owned.Quantity
                && state.Live.Positions.Single().CostThb == owned.CostThb
                && state.Live.Fills.Count == 0
                && fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0,
            "Stopped/revoked execution and fake restart never replay an unsent exit or invent external fills; wallet/engine ownership differences remain explicit until evidence-backed reconciliation."
        );

        var blocked = false;
        try
        {
            await fixture.Activate();
        }
        catch (ArgumentException error)
        {
            blocked = error.Message == "Reconcile existing owned positions before enabling the new policy.";
        }

        Check(
            blocked
                && fixture.Store.State.Mode == TradingMode.Stopped
                && fixture.Store.State.Mandate == state.Mandate
                && fixture.Store.State.Live.Positions.Single().Quantity == owned.Quantity
                && fixture.Store.State.Live.Fills.Count == 0
                && fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0,
            "A fake activation with zero/partial wallet coverage must reject the ownership mismatch without changing mandate, accounting or broker orders."
        );
    }

    private static async Task StoppedBrokerIdentityNeedsInspection(bool brokerIdentity)
    {
        using var fixture = await Pending(false);
        var original = fixture.Store.State.Live.Orders.Single() with
        {
            BrokerOrderId = brokerIdentity ? "fake-uncertain-broker-identity" : null,
            CancellationState = brokerIdentity ? null : "claimed",
        };
        var owned = fixture.Store.State.Live.Positions.Single();
        fixture.Store.State = fixture.Store.State with { Live = fixture.Store.State.Live with { Orders = [original] } };
        var inspections = 0;
        fixture.Exchange.Observation = order =>
        {
            inspections++;
            return new("unknown", order.BrokerOrderId, null, []);
        };
        await fixture.Control.Disable(false, CancellationToken.None);
        await fixture.Engine.Tick(CancellationToken.None);
        await fixture.Restart().Tick(CancellationToken.None);

        var state = fixture.Store.State;
        Check(
            state.Mode == TradingMode.Stopped
                && state.Mandate?.RevokedAt is not null
                && state.Live.Orders.Single().Status == "unknown"
                && state.Live.Orders.Single().BrokerOrderId == original.BrokerOrderId
                && state.Live.Orders.Single().CancellationState == original.CancellationState
                && inspections == 1
                && state.Live.Positions.Single().Quantity == owned.Quantity
                && state.Live.Fills.Count == 0
                && state.Fault is not null
                && fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0,
            "Disarm/restart must retain an ambiguous pending broker identity or cancellation claim for read-only inspection, never label it cancelled without broker evidence or replay it."
        );
    }

    private static async Task ZeroWalletResidualQuarantined(int scale, decimal quantity = .00001902m)
    {
        using var fixture = new AutonomousFixture();
        var residual = new TradingHolding("ETH", quantity, 1m, fixture.Time.Now.AddDays(-1), 110m);
        fixture.Store.State = fixture.Store.State with
        {
            Live = fixture.Store.State.Live with { Positions = [residual] },
        };
        var before = fixture.Store.State;
        fixture.Exchange.SellAmountScale = scale;
        fixture.Exchange.AvailableBase = 0m;
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with
        {
            Balances = [new("THB", 5000m, 5000m), new("ETH", 0m, 0m)],
        };
        var blocked = false;
        try
        {
            await fixture.Activate();
        }
        catch (ArgumentException error)
        {
            blocked = error.Message == "Reconcile existing owned positions before enabling the new policy.";
        }

        Check(
            blocked
                && fixture.Store.State == before
                && fixture.Store.State.Live.Positions.Single() == residual
                && fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0,
            "A verified zero wallet cannot cover a positive historical residual even when market precision rounds it to zero; activation stays stopped and historical quantity/cost/fills are preserved."
        );
    }

    private static async Task ReservedFunds()
    {
        using var fixture = await Pending(false);
        fixture.Exchange.TotalBase = .2m;
        fixture.Exchange.AvailableBase = .1m;
        var logger = new FakeTradingLogger();
        await Engine(fixture, logger).Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Store.State.Live.Orders.Single().Status == "pending"
                && logger.Messages.Single().Contains("AvailableFunds", StringComparison.Ordinal),
            "Insufficient available balance with full total ownership stays blocked; it does not imply liquidation or cancel the pending exit."
        );
    }

    private static async Task UncertainPeer()
    {
        using var fixture = await Pending(false);
        var pending = fixture.Store.State.Live.Orders.Single();
        var submitted = pending with
        {
            Id = Guid.NewGuid(),
            Symbol = "BTC",
            Status = "submitted",
            BrokerOrderId = "fake-existing-order",
        };
        fixture.Store.State = fixture.Store.State with
        {
            Live = fixture.Store.State.Live with { Orders = [pending, submitted] },
        };
        fixture.Exchange.Observation = _ => throw new HttpRequestException("fake-inspection-unavailable");
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0
                && fixture.Store.State.Live.Orders.Single(order => order.Id == submitted.Id).Status == "unknown"
                && fixture.Store.State.Live.Orders.Single(order => order.Id == pending.Id).Status == "pending",
            "An unavailable broker reconciliation blocks pending exits without repeating or cancelling an uncertain order."
        );
    }

    private static async Task UncertainSubmission()
    {
        using var fixture = await Pending(false);
        fixture.Exchange.FailSubmission = true;
        fixture.Exchange.Observation = _ => new("unknown", null, null, []);
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 1 && fixture.Store.State.Live.Orders.Single().Status == "unknown",
            "An uncertain attempted send is durably unknown."
        );
        fixture.Time.Now = fixture.Time.Now.AddMinutes(16);
        fixture.Markets.Books = [fixture.Book("ETH") with { Bid = 108m, Ask = 108.01m }];
        await fixture.Restart().Tick(CancellationToken.None);
        Check(fixture.Exchange.Submissions == 1, "Expiry and restart never replay a send with an uncertain outcome.");
    }

    private static TradingEngine Engine(AutonomousFixture fixture, FakeTradingLogger logger) =>
        new(
            fixture.Store,
            fixture.Markets,
            fixture.Exchange,
            fixture.Account,
            fixture.Gate,
            fixture.Time,
            autonomous: fixture.Monitor,
            adaptiveRisk: fixture.Risk,
            logger: logger
        );

    private static async Task<AutonomousFixture> Pending(bool almostExpired = true)
    {
        var fixture = new AutonomousFixture();
        await fixture.Activate();
        var state = fixture.Store.State;
        var intent = new TradingIntent(
            Guid.NewGuid(),
            "ETH",
            TradeSide.Sell,
            .2m,
            108m,
            .2m,
            almostExpired
                ? fixture.Time.Now.AddMinutes(-state.Rules.OrderLifetimeMinutes).AddSeconds(1)
                : fixture.Time.Now,
            fixture.Time.Now.ToUnixTimeSeconds() / 300 * 300 - 300,
            state.RulesHash,
            "fixture exit",
            "pending"
        );
        fixture.Store.State = state with
        {
            Live = state.Live with
            {
                CashThb = 4978m,
                Positions = [new("ETH", .2m, 22m, fixture.Time.Now.AddMinutes(-5), 110m) { StopPrice = 109m }],
                Orders = [intent],
            },
        };
        fixture.Exchange.AvailableBase = .2m;
        fixture.Markets.Books = [fixture.Book("ETH") with { Bid = 108m, Ask = 108.01m }];
        return fixture;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
