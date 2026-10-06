using System.Text.Json;
using TradingCompany.AgentRuntime;
using TradingCompany.Application;
using TradingCompany.Api;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveCompanyCoordinatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-live-company-" + Guid.NewGuid().ToString("N"));
    private static TradingAccountContext Scope => new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N"));
    private readonly Clock clock = new();

    [Fact]
    public async Task Expert_meeting_uses_owned_live_money_and_dispatches_once_after_nine()
    {
        var client = new Client();
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        clock.Now = LiveRiskTests.Now.AddMinutes(-1); // 08:59 Bangkok
        await coordinator.TickAsync();
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Intents);
        clock.Now = LiveRiskTests.Now;
        await coordinator.TickAsync();
        var intent = Assert.Single(client.Intents);
        Assert.Equal(TradingMode.Live, intent.Mode);
        Assert.Equal(LiveRiskTests.Account, intent.AccountId);
        Assert.Equal(1, intent.MandateVersion);
        Assert.Equal(1, intent.SnapshotVersion);
        Assert.Equal("BTC-THB", intent.Symbol);
        Assert.Equal(.0015m, intent.Quantity); // 15% of actual 1000 THB, ask 100000
        Assert.Equal(150m, intent.BuyBudget);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(Scope, input.AccountContext);
            Assert.Equal(1000m, input.Portfolio.Equity);
            Assert.Equal(1000m, input.Portfolio.Cash);
            Assert.Single(input.Candidates);
        });
        var restarted = Coordinator(client, gateway);
        await restarted.TickAsync();
        Assert.Single(client.Intents);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.Equal("ALREADY_ATTEMPTED", restarted.Status);
    }

    [Fact]
    public async Task Completed_meeting_summary_is_saved_and_published_on_following_tick_without_resubmitting()
    {
        var client = new Client();
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Single(client.Intents);
        Assert.Empty(client.Meetings);

        await coordinator.TickAsync();
        var saved = Assert.Single(client.Meetings);
        Assert.Equal("ORDER_PENDING", saved.Status);
        Assert.NotNull(saved.Summary);
        Assert.Contains(saved.Summary!.Reports, report => report.Role == "ceo" && report.Summary == "Review completed market bars");
        Assert.Single(client.Intents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceled_meeting_is_reported_without_retry_and_next_day_requires_fresh_authority(bool acceptedBeforeCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        var client = new Client();
        var gateway = new Gateway();
        var originalPacket = client.Packet;
        void Interrupt()
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }
        if (acceptedBeforeCancellation) client.AfterSubmit = Interrupt;
        else gateway.AfterFinal = Interrupt;
        var coordinator = Coordinator(client, gateway);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.TickAsync(cancellation.Token));

        var journal = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope);
        var interrupted = Assert.Single(journal.ReadAll());
        Assert.Equal("INTERRUPTED", interrupted.Status);
        Assert.Equal("INTERRUPTED", coordinator.Status);
        Assert.Equal(Scope, interrupted.AccountContext);
        Assert.Equal(originalPacket, client.Packet);
        var acceptedCount = acceptedBeforeCancellation ? 1 : 0;
        Assert.Equal(acceptedCount, client.Intents.Count);
        if (acceptedBeforeCancellation)
        {
            Assert.Equal(interrupted.AttemptId, Assert.Single(client.Intents).DecisionId);
            Assert.NotNull(interrupted.Summary);
        }
        else Assert.Null(interrupted.Summary);
        Assert.Empty(client.Meetings);
        var modelCalls = gateway.Inputs.Count;

        client.AfterSubmit = null;
        gateway.AfterFinal = null;
        var restarted = Coordinator(client, gateway);
        await restarted.TickAsync();

        var published = Assert.Single(client.Meetings);
        Assert.Equal(interrupted.AttemptId, published.AttemptId);
        Assert.Equal("INTERRUPTED", published.Status);
        if (acceptedBeforeCancellation)
            Assert.Contains(published.Summary!.Reports, report => report.Role == "ceo" && report.Summary == "Review completed market bars");
        Assert.Equal("ALREADY_ATTEMPTED", restarted.Status);
        Assert.Equal(acceptedCount, client.Intents.Count);
        Assert.Equal(modelCalls, gateway.Inputs.Count);

        clock.Now = clock.Now.AddDays(1);
        var fresh = originalPacket with
        {
            Snapshot = originalPacket.Snapshot with
            {
                Version = 2, ObservedAt = clock.Now,
                Quotes = [.. originalPacket.Snapshot.Quotes.Select(quote => quote with { ObservedAt = clock.Now })]
            }
        };
        foreach (var invalid in new[] { originalPacket, fresh with { Armed = false },
            fresh with { Snapshot = fresh.Snapshot with { Reconciled = false } } })
        {
            client.Packet = invalid;
            await restarted.TickAsync();
            Assert.Equal("LIVE_BLOCKED", restarted.Status);
            Assert.Equal(acceptedCount, client.Intents.Count);
            Assert.Equal(modelCalls, gateway.Inputs.Count);
            Assert.Single(journal.ReadAll());
        }

        client.Packet = fresh;
        await restarted.TickAsync();

        Assert.Equal("ORDER_PENDING", restarted.Status);
        Assert.Equal(acceptedCount + 1, client.Intents.Count);
        var nextDay = journal.Read(interrupted.BusinessDate.AddDays(1));
        Assert.NotNull(nextDay);
        Assert.Equal("ORDER_PENDING", nextDay.Status);
        Assert.Equal(nextDay.AttemptId, client.Intents[^1].DecisionId);
        Assert.NotEqual(interrupted.AttemptId, nextDay.AttemptId);
        Assert.Single(client.Meetings);
    }

    [Fact]
    public async Task Polling_version_refresh_with_unchanged_money_and_bounded_quote_uses_fresh_snapshot()
    {
        var client = new Client();
        var gateway = new Gateway();
        gateway.AfterFinal = () => client.Packet = client.Packet with
        {
            Snapshot = client.Packet.Snapshot with
            {
                Version = 2,
                ObservedAt = clock.Now,
                Quotes = [new("BTC-THB", 99990, 100010, clock.Now)]
            }
        };
        await Coordinator(client, gateway).TickAsync();
        var intent = Assert.Single(client.Intents);
        Assert.Equal(2, intent.SnapshotVersion);
        Assert.Equal(100010m, intent.LimitPrice);
        Assert.Equal(.00149985m, intent.Quantity);
        Assert.Equal(intent.Quantity * intent.LimitPrice, intent.BuyBudget);
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("position")]
    [InlineData("reserve")]
    [InlineData("turnover")]
    [InlineData("mandate")]
    public async Task Polling_owned_state_changes_never_dispatch(string defect)
    {
        var client = new Client();
        var gateway = new Gateway();
        gateway.AfterFinal = () => client.Packet = defect switch
        {
            "cash" => client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 2, CashBalance = 999, Equity = 999 } },
            "position" => client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 2, Positions = [new("BTC-THB", .0001m, 0, 100000m)], Equity = 1010 } },
            "reserve" => client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 2, CashReserved = 1 } },
            "turnover" => client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 2, DailyTurnover = 1 } },
            _ => client.Packet with { MandateVersion = 2 }
        };
        await Coordinator(client, gateway).TickAsync();
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task Polling_quote_beyond_owner_slippage_never_dispatches()
    {
        var client = new Client();
        var gateway = new Gateway
        {
            AfterFinal = () => client.Packet = client.Packet with
            { Snapshot = client.Packet.Snapshot with { Version = 2, ObservedAt = clock.Now,
                Quotes = [new("BTC-THB", 99900, 101001, clock.Now)] } }
        };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Empty(client.Intents);
        Assert.Equal("PRICE_CHANGED", coordinator.Status);
    }

    [Fact]
    public async Task Full_capital_buy_reserves_fee_inside_the_cash_limit()
    {
        var client = new Client { Packet = new Client().Packet with
        { Limits = new(1000, 1m, 3, .003m, .01m, ["BTC-THB"]) { OrderSizing = [new("BTC-THB", .01m, .00000001m, 1m, 10m)] } } };
        var gateway = new Gateway { TargetWeight = 1m };
        await Coordinator(client, gateway).TickAsync();
        var intent = Assert.Single(client.Intents);
        Assert.True(intent.BuyBudget!.Value * (1 + client.Packet.Limits.FeeReserveRate) <= 1000m);
    }

    [Fact]
    public async Task Missing_order_sizing_blocks_before_model_quota()
    {
        var client = new Client { Packet = new Client().Packet with
        { Limits = new(1000, .2m, 3, .003m, .01m, ["BTC-THB"]) } };
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("LIVE_BLOCKED", coordinator.Status);
        Assert.Empty(gateway.Inputs);
    }

    [Fact]
    public async Task Trim_quantity_is_rounded_down_to_the_broker_base_step()
    {
        var snapshot = LiveRiskTests.Snapshot with
        {
            Positions = [new("BTC-THB", .003456789123m, 0, 100000m)], Equity = 1345.6789123m
        };
        var client = new Client { Packet = new Client().Packet with { Snapshot = snapshot } };
        var gateway = new Gateway { Action = "TRIM", TargetWeight = .1m };
        await Coordinator(client, gateway).TickAsync();
        var intent = Assert.Single(client.Intents);
        Assert.Equal(OrderSide.Sell, intent.Side);
        Assert.Equal(.00245578m, intent.Quantity);
        Assert.Equal(0, decimal.Remainder(intent.Quantity, .00000001m));
    }

    [Fact]
    public async Task Zero_cash_opening_holdings_submit_fee_bounded_sell_that_passes_executor_risk()
    {
        var client = HoldingsClient();
        var gateway = new Gateway { Action = "SELL" };
        await Coordinator(client, gateway).TickAsync();

        var intent = Assert.Single(client.Intents);
        Assert.Equal(OrderSide.Sell, intent.Side);
        Assert.Equal(.00797607m, intent.Quantity);
        Assert.Equal(100000m, intent.LimitPrice);
        Assert.Null(intent.BuyBudget);
        Assert.True(LiveRiskPolicy.Evaluate(intent, client.Packet.Snapshot,
            LiveRiskTests.Mandate, LiveRiskTests.Gate, clock.Now).Allowed);
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(0m, input.Portfolio.Cash);
            Assert.Equal(1000m, input.Portfolio.Equity);
        });
    }

    [Theory]
    [InlineData("BUY")]
    [InlineData("SELL")]
    [InlineData("TRIM")]
    public async Task Actual_order_limit_caps_every_action_below_capital_allocation(string action)
    {
        var client = action == "BUY" ? new Client() : HoldingsClient();
        client.Packet = client.Packet with
        {
            Limits = client.Packet.Limits with { MaximumOrderNotional = 100m, MaximumDailyTurnover = 2000m }
        };
        var gateway = new Gateway { Action = action };
        await Coordinator(client, gateway).TickAsync();

        var intent = Assert.Single(client.Intents);
        Assert.Equal(action == "BUY" ? OrderSide.Buy : OrderSide.Sell, intent.Side);
        Assert.Equal(.000997m, intent.Quantity);
        Assert.True(LiveRiskPolicy.Evaluate(intent, client.Packet.Snapshot,
            LiveRiskTests.Mandate with { MaximumOrderNotional = 100m }, LiveRiskTests.Gate, clock.Now).Allowed);
    }

    [Fact]
    public async Task Remaining_daily_turnover_deducts_both_pending_sides_with_fees_before_sizing_sell()
    {
        var client = HoldingsClient(reservedQuantity: .0005m, cash: 1000m);
        client.Packet = client.Packet with
        {
            Limits = client.Packet.Limits with { MaximumDailyTurnover = 1000m },
            Snapshot = client.Packet.Snapshot with
            {
                CashReserved = 150.45m,
                DailyTurnover = 700m,
                PendingBuyNotional = 150m,
                PendingSellNotional = 50m,
                PendingBuySymbols = ["BTC-THB"]
            }
        };
        await Coordinator(client, new Gateway { Action = "SELL" }).TickAsync();

        var intent = Assert.Single(client.Intents);
        Assert.Equal(.00099102m, intent.Quantity);
        Assert.True(LiveRiskPolicy.Evaluate(intent, client.Packet.Snapshot,
            LiveRiskTests.Mandate with { MaximumDailyTurnover = 1000m }, LiveRiskTests.Gate, clock.Now).Allowed);
    }

    [Fact]
    public async Task Sell_clips_owned_unreserved_coins_and_floors_to_base_step()
    {
        var client = HoldingsClient(reservedQuantity: .009876543m);
        client.Packet = client.Packet with
        {
            Snapshot = client.Packet.Snapshot with { PendingSellNotional = 987.6543m }
        };
        await Coordinator(client, new Gateway { Action = "SELL" }).TickAsync();

        var intent = Assert.Single(client.Intents);
        Assert.Equal(.00012345m, intent.Quantity);
        Assert.Equal(0m, decimal.Remainder(intent.Quantity, .00000001m));
        Assert.True(LiveRiskPolicy.Evaluate(intent, client.Packet.Snapshot,
            LiveRiskTests.Mandate, LiveRiskTests.Gate, clock.Now).Allowed);
    }

    [Theory]
    [InlineData("owned-coins")]
    [InlineData("daily-limit")]
    public async Task Sell_below_minimum_quote_size_does_not_submit(string bound)
    {
        var client = HoldingsClient(quantity: bound == "owned-coins" ? .00009999m : .01m);
        if (bound == "daily-limit") client.Packet = client.Packet with
        {
            Snapshot = client.Packet.Snapshot with { DailyTurnover = 1990.01m }
        };
        var gateway = new Gateway { Action = "SELL" };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();

        Assert.NotEmpty(gateway.Inputs);
        Assert.Empty(client.Intents);
        Assert.Equal("HOLD", coordinator.Status);
    }

    [Fact]
    public async Task Legacy_missing_notional_limits_fall_back_to_capital_allocation_with_fee_headroom()
    {
        var client = HoldingsClient();
        client.Packet = client.Packet with
        {
            Limits = client.Packet.Limits with { MaximumOrderNotional = null, MaximumDailyTurnover = null }
        };
        await Coordinator(client, new Gateway { Action = "SELL" }).TickAsync();

        var intent = Assert.Single(client.Intents);
        Assert.Equal(.00997008m, intent.Quantity);
        Assert.True(intent.Quantity * intent.LimitPrice * (1 + client.Packet.Limits.FeeReserveRate)
            <= client.Packet.Limits.CapitalAllocation);
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("invalid-output")]
    [InlineData("model-down")]
    [InlineData("stop-during-meeting")]
    [InlineData("quote-expired")]
    public async Task Missing_authority_or_invalid_advice_never_dispatches(string scenario)
    {
        var client = new Client();
        var gateway = new Gateway { Scenario = scenario };
        gateway.AfterFinal = () =>
        {
            if (scenario == "stop-during-meeting") client.Packet = client.Packet with { Stopped = true };
            if (scenario == "quote-expired") clock.Now = clock.Now.AddSeconds(31);
        };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Empty(client.Intents);
        Assert.NotEqual("DISABLED", coordinator.Status);
    }

    [Theory]
    [InlineData("disarmed")]
    [InlineData("stopped")]
    [InlineData("unreconciled")]
    [InlineData("wrong-account")]
    [InlineData("stale")]
    [InlineData("zero-budget")]
    [InlineData("invalid-equity")]
    [InlineData("negative-loss")]
    [InlineData("negative-pending")]
    [InlineData("duplicate-position")]
    [InlineData("duplicate-quote")]
    public async Task Invalid_live_state_blocks_research_and_model_quota(string scenario)
    {
        var client = new Client();
        client.Packet = scenario switch
        {
            "disarmed" => client.Packet with { Armed = false },
            "stopped" => client.Packet with { Stopped = true },
            "unreconciled" => client.Packet with { Snapshot = client.Packet.Snapshot with { Reconciled = false } },
            "wrong-account" => client.Packet with { AccountContext = Scope with { AccountId = Guid.NewGuid().ToString("N") } },
            "zero-budget" => client.Packet with { Limits = client.Packet.Limits with { CapitalAllocation = 0 } },
            "invalid-equity" => client.Packet with { Snapshot = client.Packet.Snapshot with { Equity = 1100m } },
            "negative-loss" => client.Packet with { Snapshot = client.Packet.Snapshot with { DailyLoss = -1m } },
            "negative-pending" => client.Packet with { Snapshot = client.Packet.Snapshot with { PendingBuyNotional = -1m } },
            "duplicate-position" => client.Packet with { Snapshot = client.Packet.Snapshot with
                { Positions = [new("BTC-THB", .001m, 0, 100000m), new("BTC-THB", .001m, 0, 100000m)], Equity = 1200m } },
            "duplicate-quote" => client.Packet with { Snapshot = client.Packet.Snapshot with
                { Quotes = [.. client.Packet.Snapshot.Quotes, client.Packet.Snapshot.Quotes[0] with { Ask = 0 }] } },
            _ => client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now.AddSeconds(-31) } }
        };
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("LIVE_BLOCKED", coordinator.Status);
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Intents);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("synthetic")]
    [InlineData("missing")]
    public async Task Incomplete_or_non_market_evidence_never_reaches_agents(string scenario)
    {
        var client = new Client();
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway, new Market { Scenario = scenario });
        await coordinator.TickAsync();
        Assert.Equal("RESEARCH_UNAVAILABLE", coordinator.Status);
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Intents);
    }

    private LiveCompanyCoordinator Coordinator(Client client, Gateway gateway, Market? market = null)
    {
        var runner = new AiCompanyMeetingRunner(gateway,
            new JsonAgentDecisionArtifactStore(Path.Combine(root, "artifacts")),
            new(true, "test-model", "medium", "company-v1", "live-v1", 600, 8, 20000, TimeSpan.FromSeconds(30)));
        return new(client, market ?? new Market(), runner,
            new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope), clock, LiveRiskTests.Account);
    }

    private static Client HoldingsClient(decimal quantity = .01m, decimal reservedQuantity = 0m, decimal cash = 0m)
    {
        var client = new Client();
        client.Packet = client.Packet with
        {
            Snapshot = client.Packet.Snapshot with
            {
                CashBalance = cash,
                Positions = [new("BTC-THB", quantity, reservedQuantity, 100000m)],
                Equity = cash + quantity * 100000m,
                Quotes = [new("BTC-THB", 100000m, 100000m, LiveRiskTests.Now)]
            },
            Limits = client.Packet.Limits with { MaximumOrderNotional = 800m, MaximumDailyTurnover = 2000m }
        };
        return client;
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = LiveRiskTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Client : ILiveCompanyClient
    {
        public LiveResearchPacket Packet { get; set; } = new(Scope, 1, LiveRiskTests.Snapshot,
            new(1000m, .2m, 3, .003m, .01m, ["BTC-THB"]) { OrderSizing =
                [new("BTC-THB", .01m, .00000001m, 1m, 10m)] }, true, false);
        public List<LiveIntent> Intents { get; } = [];
        public List<DailyMeetingAttempt> Meetings { get; } = [];
        public Action? AfterSubmit { get; set; }
        public Task SaveMeetingAsync(DailyMeetingAttempt attempt, CancellationToken cancellationToken)
        { Meetings.Add(attempt); return Task.CompletedTask; }
        public Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken) => Task.FromResult(Packet);
        public Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            AfterSubmit?.Invoke();
            return Task.FromResult(new LiveIntentReceipt(intent, LiveOrderState.Acknowledged, []));
        }
    }
    internal sealed class Gateway : ITradingAgentGateway
    {
        public string Scenario { get; init; } = "buy";
        public string Action { get; init; } = "BUY";
        public decimal TargetWeight { get; init; } = .15m;
        public Action? AfterFinal { get; set; }
        public List<AiCompanyMeetingInput> Inputs { get; } = [];
        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken)
        {
            if (Scenario == "model-down") throw new AgentModelUnavailableException("fixture");
            using var document = JsonDocument.Parse(request.InputJson);
            var input = document.RootElement.GetProperty("meeting").Deserialize<AiCompanyMeetingInput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Inputs.Add(input);
            if (request.Role == "portfolio-final") AfterFinal?.Invoke();
            var output = JsonSerializer.Serialize(new
            {
                role = request.Role, summary = "Review completed market bars", recommendations = new[] { "Review evidence" },
                risks = new[] { "Price changes" }, evidenceIds = input.Candidates.Select(c => c.EvidenceId),
                dataCutoff = input.DataCutoff.ToString("yyyy-MM-dd"), evidenceHash = input.EvidenceHash,
                proposals = request.Role != "portfolio-final" || Scenario == "hold" ? [] : new[]
                {
                    new { action = Action, symbol = Scenario == "invalid-output" ? "INVENTED" : "BTC-THB", targetWeight = TargetWeight,
                        confidence = .8m, thesis = "Supplied evidence supports entry", invalidation = "Trend deteriorates" }
                }
            });
            return Task.FromResult(new AgentModelResponse(output, "test-response", request.Model, 100, 50, TimeSpan.Zero));
        }
    }
    internal sealed class Market : IHistoricalMarketEvidenceProvider
    {
        public string Scenario { get; init; } = "normal";
        public Task<IReadOnlyList<HistoricalBar>> GetAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<HistoricalMarketEvidence> GetEvidenceAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
        {
            var count = Scenario == "missing" ? 10 : 250;
            var bars = Enumerable.Range(0, count).Select(i => new HistoricalBar(end.AddDays(i - count + 1), symbol,
                75000m + i, 76000m + i, 74000m + i, 75500m + i, 1000)).ToArray();
            if (Scenario == "future") bars[^1] = bars[^1] with { Date = end.AddDays(1) };
            return Task.FromResult(new HistoricalMarketEvidence(bars, bars, [], Scenario == "synthetic" ? "synthetic-demo" : "bitkub-daily", "THB"));
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
