using System.Text.Json;
using System.Text.Json.Serialization;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class AutonomousCompatibilityChecks
{
    public static async Task Run()
    {
        var day = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.FromHours(7));
        using (var gate = new SemaphoreSlim(1, 1))
        {
            var store = new FakeTradingDeliveryStore(TradingStateFactory.Create(day));
            var clock = new OrderCheckTime { Now = day.AddHours(23).AddMinutes(59) };
            var notifier = new FakeTradingNotifier();
            notifier.BeforeSend = () =>
            {
                Check(gate.Wait(0), "Notification network calls must not hold the engine gate.");
                gate.Release();
            };
            var delivery = new TradingReportDelivery(store, notifier, clock, gate);
            await gate.WaitAsync();
            var pending = delivery.Tick(CancellationToken.None);
            Check(!pending.IsCompleted && notifier.Sends == 0, "Report writes wait for the active engine tick.");
            gate.Release();
            await pending;
            Check(notifier.Sends == 1, "The daily report resumes after the engine releases its gate.");
        }

        using (var fixture = new AutonomousFixture())
        {
            await fixture.Activate();
            fixture.Time.Now = fixture.Time.Now.AddSeconds(50);
            fixture.Evidence.Evidence = fixture.Evidence.Evidence with
            {
                ObservedAt = fixture.Time.Now,
                Balances = [new Balance("THB", 3900, 3900), new Balance("ETH", 10, 10)],
            };
            fixture.Store.State = fixture.Store.State with
            {
                Live = fixture.Store.State.Live with
                {
                    CashThb = 3900,
                    Positions = [new TradingHolding("ETH", 10, 1100, fixture.Time.Now, 110)],
                },
            };
            fixture.Markets.Books =
            [
                fixture.Book("ETH") with
                {
                    Bid = 113,
                    Ask = 114,
                    Enabled = false,
                    SellEnabled = true,
                },
            ];
            var pending = new TaskCompletionSource();
            fixture.Evidence.BeforeRead = pending;
            try
            {
                await fixture.Engine.Tick(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
                Check(
                    fixture.Exchange.Submissions == 1
                        && fixture.Store.State.Live.Orders.Single().Side == TradeSide.Sell,
                    "Slow account research and frozen buys must not delay a permitted owned exit."
                );
            }
            finally
            {
                pending.SetResult();
            }
        }

        using (var fixture = new AutonomousFixture())
        {
            fixture.Store.State = fixture.Store.State with { Mode = TradingMode.Paper };
            var symbols = Enumerable
                .Range(1, 20)
                .Select(index => "C" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            fixture.Markets.Universe = new(fixture.Time.Now, symbols, 20, 0, null);
            fixture.Markets.Books = symbols.Select(symbol => fixture.Book(symbol, false)).ToArray();
            await fixture.Engine.Tick(CancellationToken.None);
            fixture.Time.Now = fixture.Time.Now.AddSeconds(10);
            fixture.Markets.Books = symbols.Select(symbol => fixture.Book(symbol, false)).ToArray();
            await fixture.Engine.Tick(CancellationToken.None);
            Check(
                fixture.Store.State.Autonomy?.Markets.Count == 20 && fixture.Exchange.Submissions == 0,
                "Bounded detailed scans must rotate beyond the first sixteen markets."
            );
        }

        using (var fixture = new AutonomousFixture())
        {
            await fixture.Activate();
            var orders = new FakeOrderStore();
            var broker = new FakeOrderBroker();
            var draft = await new CreateOrderHandler(orders, broker, fixture.Time).Handle(
                new("BTC", TradeSide.Buy, "100", "100", "Human fixture"),
                CancellationToken.None
            );
            var blocked = false;
            try
            {
                await new ApproveOrderHandler(orders, broker, fixture.Time, trading: fixture.Store).Handle(
                    new(draft.Id, true),
                    CancellationToken.None
                );
            }
            catch (OrderValidationException)
            {
                blocked = true;
            }

            Check(
                blocked && broker.Submissions == 0,
                "Manual approval and autonomous execution cannot spend concurrently."
            );
        }

        using (var fixture = new AutonomousFixture())
        {
            var orders = new FakeOrderStore();
            var draft = await new CreateOrderHandler(orders, new FakeOrderBroker(), fixture.Time).Handle(
                new("BTC", TradeSide.Buy, "100", "100", "Legacy uncertain fixture"),
                CancellationToken.None
            );
            await orders.Finish(draft.Id, OrderState.Unknown, null, "Uncertain fixture", CancellationToken.None);
            var control = new TradingControl(
                fixture.Store,
                fixture.Exchange,
                fixture.Markets,
                fixture.Account,
                new("owner-token"),
                fixture.Gate,
                fixture.Time,
                reviewedOrders: orders,
                adaptiveRisk: fixture.Risk
            );
            var blocked = false;
            try
            {
                await control.Activate(
                    fixture.Account.AccountKey,
                    fixture.Store.State.RulesHash,
                    fixture.Time.Now.AddHours(1),
                    "enable-auto-trading",
                    CancellationToken.None
                );
            }
            catch (ArgumentException)
            {
                blocked = true;
            }

            Check(
                blocked && fixture.Store.State.Mode == TradingMode.Stopped,
                "Unknown legacy submissions block activation."
            );
        }

        var options = PortfolioJson.CreateOptions();
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter<TradingMode>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<TradingAction>(JsonNamingPolicy.CamelCase));
        var legacy = TradingStateFactory.Create(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));
        var shape = JsonSerializer.SerializeToElement(legacy, options);
        Check(
            !shape.TryGetProperty("autonomy", out _)
                && !shape.TryGetProperty("observedFundingFlows", out _)
                && !shape.TryGetProperty("shadows", out _)
                && !shape.GetProperty("rules").TryGetProperty("adaptive", out _),
            "Inactive additive fields must not break strict legacy readers before owner migration."
        );
        var roundTrip =
            shape.Deserialize<TradingState>(options)
            ?? throw new InvalidOperationException("Legacy round trip failed.");
        Check(
            roundTrip.RulesHash == legacy.RulesHash && roundTrip.Shadows.Count == 0 && roundTrip.Mandate is null,
            "Legacy JSON keeps its original rules hash and no new authority."
        );
        Console.WriteLine("Autonomous compatibility, fair scan and independent exit checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
