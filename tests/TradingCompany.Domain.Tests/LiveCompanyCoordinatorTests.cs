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
    public async Task One_meeting_can_register_three_distinct_plans_without_sending_broker_orders()
    {
        var client = new Client();
        string[] symbols = ["BTC-THB", "ETH-THB", "SOL-THB"];
        client.Packet = client.Packet with { Limits = client.Packet.Limits with { AllowedSymbols = symbols,
            OrderSizing = symbols.Select(s => new LiveOrderSizing(s, .01m, .00000001m, 1, 10)).ToArray() },
            Snapshot = client.Packet.Snapshot with { Quotes = symbols.Select(s => new LiveQuote(s, 100000, 100000, clock.Now)).ToArray() } };
        var gateway = new Gateway { ProposalSymbols = symbols };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("PLAN_REGISTERED", coordinator.Status);
        Assert.Equal(symbols, client.Plans.Select(p => p.Symbol));
        Assert.Equal(3, client.Plans.Select(p => p.Id).Distinct().Count());
        Assert.Single(client.Plans.Select(p => p.DecisionId).Distinct());
        Assert.Empty(client.Intents);
        await coordinator.TickAsync();
        Assert.Equal(3, client.Plans.Count);
    }

    [Fact]
    public async Task Live_candidates_include_rejected_alternatives_and_actual_costs_instead_of_only_cash_fallback()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        await Coordinator(client, gateway).TickAsync();
        Assert.Equal("HOLD", new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll().Single().Status);
        Assert.Empty(client.Intents);
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(JsonSerializer.Serialize(client.Packet.Limits), JsonSerializer.Serialize(input.ExecutionLimits));
            var candidate = Assert.Single(input.Candidates);
            var comparison = Assert.IsType<AiCompanyOpportunityEvidence>(candidate.OpportunityComparison);
            Assert.Equal(StrategyKind.Cash, candidate.Strategy);
            Assert.Equal(0m, candidate.OutOfSampleReturn);
            Assert.Equal(0m, comparison.CashBenchmarkReturn);
            Assert.Equal(0m, comparison.HistoricalCashOpportunityCost);
            Assert.Equal(client.Packet.Limits.FeeReserveRate, comparison.Costs.FeeRate);
            Assert.Equal(client.Packet.Limits.MaximumSlippage, comparison.Costs.SlippageRate);
            Assert.Equal(4, comparison.Strategies.Count);
            Assert.All(comparison.Strategies, strategy =>
            {
                Assert.False(strategy.Promoted);
                Assert.NotNull(strategy.RejectionReason);
                Assert.Equal(comparison.FoldCount, strategy.FoldNetReturns.Count);
            });
            Assert.Contains(comparison.Strategies, strategy => strategy.NetReturn < 0m);
            Assert.True(comparison.ValidationStart < comparison.ValidationEnd);
        });
    }

    [Fact]
    public async Task Expert_meeting_uses_owned_live_money_and_dispatches_once_after_nine()
    {
        var client = new Client();
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        clock.Now = LiveRiskTests.Now.AddMinutes(-1); // 08:59 Bangkok
        await coordinator.TickAsync();
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Plans);
        clock.Now = LiveRiskTests.Now;
        await coordinator.TickAsync();
        var intent = Assert.Single(client.Plans);
        Assert.Empty(client.Intents);
        Assert.Equal(LiveRiskTests.Account, intent.AccountId);
        Assert.Equal(1, intent.MandateVersion);
        Assert.Equal(1, intent.SnapshotVersion);
        Assert.Equal("BTC-THB", intent.Symbol);
        Assert.Equal(.00149551m, intent.Plan.Quantity); // 15% includes the fee reserve
        Assert.Empty(client.Intents);
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
        Assert.Single(client.Plans);
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
        Assert.Single(client.Plans);
        Assert.Empty(client.Meetings);

        await coordinator.TickAsync();
        var saved = Assert.Single(client.Meetings);
        Assert.Equal("PLAN_REGISTERED", saved.Status);
        Assert.NotNull(saved.Summary);
        Assert.Contains(saved.Summary!.Reports, report => report.Role == "ceo" && report.Summary == "Review completed market bars");
        Assert.Single(client.Plans);
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
        Assert.Equal(acceptedCount, client.Plans.Count);
        if (acceptedBeforeCancellation)
        {
            Assert.Equal(interrupted.AttemptId, Assert.Single(client.Plans).DecisionId);
            Assert.NotNull(interrupted.Summary);
        }
        else Assert.Empty(Assert.IsType<DailyMeetingSummary>(interrupted.Summary).Reports);
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
        Assert.Equal(acceptedCount, client.Plans.Count);
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
        foreach (var invalid in new[] { originalPacket,
            fresh with { Snapshot = fresh.Snapshot with { Reconciled = false } } })
        {
            client.Packet = invalid;
            await restarted.TickAsync();
            Assert.Equal("LIVE_BLOCKED", restarted.Status);
            Assert.Equal(acceptedCount, client.Plans.Count);
            Assert.Equal(modelCalls, gateway.Inputs.Count);
            Assert.Single(journal.ReadAll());
        }

        clock.Now = clock.Now.AddMinutes(5);
        client.Packet = fresh with { Snapshot = fresh.Snapshot with { ObservedAt = clock.Now,
            Quotes = fresh.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        await restarted.TickAsync();

        Assert.Equal("PLAN_REGISTERED", restarted.Status);
        Assert.Equal(acceptedCount + 1, client.Plans.Count);
        var nextDay = journal.Read(interrupted.BusinessDate.AddDays(1));
        Assert.NotNull(nextDay);
        Assert.Equal("PLAN_REGISTERED", nextDay.Status);
        Assert.Equal(nextDay.AttemptId, client.Plans[^1].DecisionId);
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
        var intent = Assert.Single(client.Plans);
        Assert.Equal(2, intent.SnapshotVersion);
        Assert.Equal(100000m, intent.Plan.EntryPrice); // advice retains its original entry ceiling
        Assert.Equal(.00149551m, intent.Plan.Quantity);
        Assert.Empty(client.Intents);
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
        var intent = Assert.Single(client.Plans);
        Assert.True((intent.Plan.Quantity * intent.Plan.EntryPrice) * (1 + client.Packet.Limits.FeeReserveRate) <= 1000m);
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

        if (action == "BUY")
        {
            var plan = Assert.Single(client.Plans);
            Assert.Empty(client.Intents);
            Assert.True(plan.Plan.Quantity * plan.Plan.EntryPrice * (1 + client.Packet.Limits.FeeReserveRate) <= 100m);
            return;
        }
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
    public async Task Full_portfolio_research_recycles_capital_and_sells_all_verified_owned_quantity()
    {
        var client = HoldingsClient(quantity: .02m);
        client.Packet = client.Packet with
        {
            Snapshot = client.Packet.Snapshot with { DailyTurnover = 10000 },
            Limits = client.Packet.Limits with { UseFullPortfolio = true, CapitalAllocation = 2000,
                MaximumOrderNotional = 2000, MaximumDailyTurnover = null, MaximumPositionWeight = 1 }
        };
        var gateway = new Gateway { Action = "SELL" };
        await Coordinator(client, gateway).TickAsync();
        var intent = Assert.Single(client.Intents);
        Assert.Equal(.02m, intent.Quantity);
        Assert.All(gateway.Inputs, input => Assert.Contains("capital can be recycled", input.Mandate));
        Assert.True(LiveRiskPolicy.Evaluate(intent, client.Packet.Snapshot,
            LiveRiskTests.Mandate with { UseFullPortfolio = true, MaximumOrderNotional = 1000,
                MaximumDailyTurnover = 1000, MaximumDailyLoss = 1000, MaximumPortfolioExposure = 1,
                MaximumDrawdown = 1 }, LiveRiskTests.Gate, clock.Now).Allowed);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Paused_execution_still_meets_and_saves_advice_without_dispatching(bool stopped)
    {
        var client = new Client { Packet = new Client().Packet with { Armed = false, Stopped = stopped } };
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("READ_ONLY", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.All(gateway.Inputs, input => Assert.Contains("advisory only", input.Mandate));
        Assert.Empty(client.Intents);
        Assert.Empty(coordinator.TradingUniverse);
        await coordinator.TickAsync();
        var saved = Assert.Single(client.Meetings);
        Assert.True(saved.Summary!.AdvisoryOnly);
        Assert.Equal(new[] { "EXECUTION_UNAVAILABLE" }, saved.Summary.AdvisoryReasons);
        Assert.Equal(8, saved.Summary.Reports.Count);
        Assert.Empty(client.Intents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_bad_symbol_is_excluded_while_verified_symbols_keep_their_existing_authority(bool paused)
    {
        var client = new Client { Packet = new Client().Packet with
        {
            Armed = !paused, Limits = new Client().Packet.Limits with { ResearchSymbols = ["BTC-THB", "SOL-THB"] },
            ResearchQuotes = [new("BTC-THB", 100000, 100000, clock.Now), new("SOL-THB", 3000, 3000, clock.Now)]
        }};
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway, new Market { FailedSymbol = "SOL-THB" });
        await coordinator.TickAsync();
        Assert.Equal(paused ? "READ_ONLY" : "PLAN_REGISTERED", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.All(gateway.CandidateSymbols, symbols => Assert.Equal("BTC-THB", Assert.Single(symbols)));
        Assert.Equal(new LiveMeetingIssue("MARKET_HISTORY_UNAVAILABLE", "SOL-THB"), Assert.Single(coordinator.Issues));
        Assert.Equal(paused ? 0 : 1, client.Plans.Count);
        Assert.All(client.Plans, i => Assert.Equal("BTC-THB", i.Symbol));
        await coordinator.TickAsync();
        var saved = Assert.Single(client.Meetings);
        Assert.Equal(paused, saved.Summary!.AdvisoryOnly);
        if (paused) Assert.Equal(new[] { "EXECUTION_UNAVAILABLE" }, saved.Summary.AdvisoryReasons);
        else Assert.Null(saved.Summary.AdvisoryReasons);
        Assert.Equal(coordinator.Issues, saved.Summary.Issues);
        Assert.Contains("Market-history gaps exclude only their named symbols", gateway.Inputs[0].Mandate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_unowned_history_gaps_keep_healthy_candidate_scoped(bool armed)
    {
        var basis = new Client().Packet;
        var symbols = new[] { "BTC-THB", "USDT-THB", "XRP-THB" };
        var quotes = symbols.Select(s => new LiveQuote(s, 100000, 100000, clock.Now)).ToArray();
        var client = new Client { Packet = basis with
        {
            Armed = armed,
            Limits = basis.Limits with { AllowedSymbols = ["BTC-THB"], ResearchSymbols = symbols,
                OrderSizing = [new("BTC-THB", .01m, .00000001m, 1m, 10m)] },
            Snapshot = basis.Snapshot with { Quotes = quotes }, ResearchQuotes = quotes
        }};
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway, new Market { FailedSymbols = ["USDT-THB", "XRP-THB"] });
        await coordinator.TickAsync();

        Assert.Equal(armed ? "PLAN_REGISTERED" : "READ_ONLY", coordinator.Status);
        Assert.Equal(armed ? 1 : 0, client.Plans.Count);
        Assert.Equal(symbols.Skip(1).Select(s => new LiveMeetingIssue("MARKET_HISTORY_UNAVAILABLE", s)),
            coordinator.Issues);
        Assert.All(gateway.CandidateSymbols, candidates => Assert.Equal("BTC-THB", Assert.Single(candidates)));
        Assert.All(gateway.Inputs, input => Assert.Contains("Market-history gaps exclude only their named symbols", input.Mandate));
        if (armed) Assert.DoesNotContain("advisory only", gateway.Inputs[0].Mandate);
        else Assert.Contains("executor readiness was unavailable", gateway.Inputs[0].Mandate);
        await coordinator.TickAsync();
        var saved = Assert.Single(client.Meetings).Summary!;
        if (armed) Assert.Null(saved.AdvisoryReasons);
        else Assert.Equal(new[] { "EXECUTION_UNAVAILABLE" }, saved.AdvisoryReasons);
    }

    [Fact]
    public async Task Missing_history_for_an_owned_position_keeps_the_entire_round_advisory_only()
    {
        var basis = new Client().Packet;
        var quotes = new[] { new LiveQuote("BTC-THB", 100000, 100000, clock.Now), new LiveQuote("SOL-THB", 3000, 3000, clock.Now) };
        var client = new Client { Packet = basis with
        {
            Limits = basis.Limits with { AllowedSymbols = ["BTC-THB", "SOL-THB"], ResearchSymbols = ["BTC-THB", "SOL-THB"],
                OrderSizing = [.. basis.Limits.OrderSizing, new("SOL-THB", .01m, .00000001m, 1m, 10m)] },
            Snapshot = basis.Snapshot with { Positions = [new("SOL-THB", .1m, 0, 3000)], Equity = 1300, Quotes = quotes },
            ResearchQuotes = quotes
        }};
        var gateway = new Gateway(); var coordinator = Coordinator(client, gateway, new Market { FailedSymbol = "SOL-THB" });
        await coordinator.TickAsync();
        Assert.Equal("READ_ONLY", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.Empty(client.Intents);
        Assert.Contains("a held asset is missing verified history", gateway.Inputs[0].Mandate);
        await coordinator.TickAsync();
        Assert.Equal(new[] { "OWNED_MARKET_HISTORY_UNAVAILABLE" }, Assert.Single(client.Meetings).Summary!.AdvisoryReasons);
    }

    [Fact]
    public async Task Missing_history_retries_after_backoff_without_consuming_a_meeting_slot()
    {
        var client = new Client(); var gateway = new Gateway(); var market = new Market { Scenario = "missing" };
        var coordinator = Coordinator(client, gateway, market, continuous: true);
        await coordinator.TickAsync();
        Assert.Equal("RESEARCH_UNAVAILABLE", coordinator.Status);
        Assert.Equal(clock.Now.AddMinutes(5), coordinator.NextRetryAt);
        Assert.Empty(new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll());
        market.Scenario = "normal";
        await coordinator.TickAsync();
        Assert.Empty(gateway.Inputs);
        clock.Now = clock.Now.AddMinutes(5);
        Freshen(client);
        await coordinator.TickAsync();
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.Single(client.Plans);
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task Failed_account_read_is_visible_and_research_recovers_without_arming()
    {
        var client = new Client { ReadFailures = 1, Packet = new Client().Packet with { Armed = false } };
        var reads = 0;
        client.BeforeRead = () => reads++;
        var gateway = new Gateway(); var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync();
        Assert.Equal("LIVE_UNAVAILABLE", coordinator.Status);
        Assert.Equal(new LiveMeetingIssue("LIVE_READ_UNAVAILABLE"), Assert.Single(coordinator.Issues));
        Assert.Equal(clock.Now.AddSeconds(30), coordinator.NextRetryAt);
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Meetings);
        Assert.Empty(client.Plans);
        clock.Now = clock.Now.AddSeconds(29); Freshen(client);
        await coordinator.TickAsync();
        Assert.Equal(1, reads);
        Assert.Empty(gateway.Inputs);
        clock.Now = clock.Now.AddSeconds(1); Freshen(client);
        await coordinator.TickAsync();
        Assert.Equal("READ_ONLY", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.False(client.Packet.Armed);
        Assert.Empty(client.Intents);
        Assert.Empty(client.Plans);
        Assert.Single(new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Stale_reconciled_data_retries_in_thirty_seconds_without_consuming_the_meeting_or_arming(bool staleQuotesOnly, bool afterHistory)
    {
        var client = new Client { Packet = new Client().Packet with { Armed = false } };
        var gateway = new Gateway();
        Freshen(client);
        var stalePacket = client.Packet with { Snapshot = client.Packet.Snapshot with
        {
            ObservedAt = staleQuotesOnly ? clock.Now : clock.Now.AddSeconds(-31),
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with
                { ObservedAt = staleQuotesOnly ? clock.Now.AddSeconds(-31) : clock.Now }).ToArray()
        } };
        if (afterHistory)
        {
            var reads = 0;
            client.BeforeRead = () => { if (++reads == 2) client.Packet = stalePacket; };
        }
        else client.Packet = stalePacket;
        var limits = JsonSerializer.Serialize(client.Packet.Limits);
        var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync();
        Assert.Equal("LIVE_BLOCKED", coordinator.Status);
        Assert.Equal(clock.Now.AddSeconds(30), coordinator.NextRetryAt);
        Assert.Empty(gateway.Inputs);
        Assert.Empty(new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll());
        client.BeforeRead = null;
        clock.Now += TimeSpan.FromSeconds(29); Freshen(client);
        await coordinator.TickAsync();
        Assert.Empty(gateway.Inputs);
        clock.Now += TimeSpan.FromSeconds(1); Freshen(client);
        await coordinator.TickAsync();
        Assert.Equal("READ_ONLY", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.False(client.Packet.Armed);
        Assert.Equal(limits, JsonSerializer.Serialize(client.Packet.Limits));
        Assert.Empty(client.Plans); Assert.Empty(client.Intents);
    }

    private void Freshen(Client client) => client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with
    { ObservedAt = clock.Now, Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };

    [Fact]
    public async Task A_paused_round_cannot_gain_dispatch_authority_while_the_agents_are_running()
    {
        var client = new Client { Packet = new Client().Packet with { Armed = false } };
        var gateway = new Gateway { AfterFinal = () => client.Packet = client.Packet with { Armed = true } };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.True(client.Packet.Armed);
        Assert.Equal("READ_ONLY", coordinator.Status);
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task A_model_cannot_propose_an_authorized_symbol_whose_evidence_was_quarantined()
    {
        var basis = new Client().Packet;
        var quotes = new[] { new LiveQuote("BTC-THB", 100000, 100000, clock.Now), new LiveQuote("SOL-THB", 3000, 3000, clock.Now) };
        var client = new Client { Packet = basis with
        {
            Limits = basis.Limits with { AllowedSymbols = ["BTC-THB", "SOL-THB"], ResearchSymbols = ["BTC-THB", "SOL-THB"],
                OrderSizing = [.. basis.Limits.OrderSizing, new("SOL-THB", .01m, .00000001m, 1m, 10m)] },
            Snapshot = basis.Snapshot with { Quotes = quotes }, ResearchQuotes = quotes
        }};
        var gateway = new Gateway { ProposalSymbol = "SOL-THB" };
        var coordinator = Coordinator(client, gateway, new Market { FailedSymbol = "SOL-THB" });
        await coordinator.TickAsync();
        Assert.NotEmpty(gateway.Inputs);
        Assert.Empty(client.Intents);
        Assert.DoesNotContain(gateway.CandidateSymbols.SelectMany(s => s), s => s == "SOL-THB");
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

    [Fact]
    public async Task Researches_all_symbols_but_only_allowed_symbol_can_execute()
    {
        var client = new Client { Packet = new Client().Packet with
        {
            Limits = new(1000, .2m, 3, .003m, .01m, ["BTC-THB"]) { ResearchSymbols = ["BTC-THB", "ETH-THB"], OrderSizing = [new("BTC-THB", .01m, .00000001m, 1m, 10m)] },
            ResearchQuotes = [new("BTC-THB", 100000, 100000, LiveRiskTests.Now), new("ETH-THB", 3000, 3000, LiveRiskTests.Now)]
        }};
        var gateway = new Gateway { ProposalSymbol = "ETH-THB" };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal(2, gateway.Inputs[0].Candidates.Count);
        Assert.Contains(gateway.Inputs[0].Candidates, c => !c.ExecutionAllowed && c.CurrentQuote!.Symbol == "ETH-THB");
        Assert.Equal("INVALID_FINAL", coordinator.Status);
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task Continuous_reviews_skip_stable_hour_and_run_after_price_change_once()
    {
        var client = new Client(); var gateway = new Gateway(); var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync();
        clock.Now = LiveRiskTests.Now.AddHours(1);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        await coordinator.TickAsync();
        Assert.Equal("WATCHING", coordinator.Status);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 2,
            ObservedAt = clock.Now, Quotes = [new("BTC-THB", 98000, 98000, clock.Now)] }, ResearchQuotes = [new("BTC-THB", 98000, 98000, clock.Now)] };
        await coordinator.TickAsync();
        Assert.Equal(11, gateway.Inputs.Count);
        Assert.Equal(new[] { "portfolio-initial", "risk-critic", "portfolio-final" }, gateway.Roles.Skip(8));
        await coordinator.TickAsync();
        Assert.Equal("ALREADY_ATTEMPTED", coordinator.Status);
        var restarted = Coordinator(client, gateway, continuous: true);
        await restarted.TickAsync();
        Assert.Equal("ALREADY_ATTEMPTED", restarted.Status);
    }

    [Fact]
    public async Task Allowed_researched_eth_buy_uses_its_current_quote_and_sizing()
    {
        var client = new Client { Packet = new Client().Packet with
        {
            Limits = new(1000, .2m, 3, .003m, .01m, ["ETH-THB"]) { ResearchSymbols = ["ETH-THB"], OrderSizing = [new("ETH-THB", .001m, .000001m, 1m, 1m)] },
            ResearchQuotes = [new("ETH-THB", 3000, 3000, LiveRiskTests.Now)],
            Snapshot = LiveRiskTests.Snapshot with { Quotes = [new("ETH-THB", 3000, 3000, LiveRiskTests.Now)] }
        }};
        var gateway = new Gateway { ProposalSymbol = "ETH-THB" };
        await Coordinator(client, gateway).TickAsync();
        var intent = Assert.Single(client.Plans);
        Assert.Equal("ETH-THB", intent.Symbol);
        Assert.Equal(3000m, intent.Plan.EntryPrice);
    }

    [Fact]
    public async Task Continuous_review_runs_when_owned_cash_changes()
    {
        var client = new Client(); var gateway = new Gateway(); var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync();
        clock.Now = LiveRiskTests.Now.AddHours(1);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 2, ObservedAt = clock.Now, CashBalance = 999, Equity = 999,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        await coordinator.TickAsync();
        Assert.Equal(11, gateway.Inputs.Count);
    }

    [Fact]
    public async Task Malformed_research_quotes_block_before_model_quota()
    {
        var client = new Client { Packet = new Client().Packet with { ResearchQuotes = [new("BTC-THB", 100000, 100000, LiveRiskTests.Now), new("BTC-THB", 100000, 100000, LiveRiskTests.Now)] } };
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("LIVE_BLOCKED", coordinator.Status);
        Assert.Empty(gateway.Inputs);
    }

    [Fact]
    public async Task Broad_authority_with_sparse_quotes_uses_only_eight_deterministic_candidates()
    {
        var client = DynamicClient();
        client.Packet = client.Packet with { ResearchQuotes = client.Packet.ResearchQuotes.Take(8).ToArray(),
            Snapshot = client.Packet.Snapshot with { Quotes = client.Packet.Snapshot.Quotes.Take(8).ToArray() } };
        var gateway = new Gateway { Scenario = "hold" };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("HOLD", coordinator.Status);
        Assert.All(gateway.CandidateSymbols, symbols => Assert.Equal(client.Packet.Limits.ResearchSymbols.Take(8), symbols));
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(12, input.ExecutionLimits!.AllowedSymbols.Count);
            Assert.Equal(8, input.Candidates.Count);
        });
        Assert.Empty(client.Intents);
        Assert.Empty(client.Plans);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public async Task Broad_review_snapshot_keeps_only_candidates_and_all_holdings_without_outside_market_reviews(int heldCount)
    {
        var client = DynamicClient(247);
        var positions = client.Packet.Limits.AllowedSymbols.Take(heldCount).Select(s => new LivePosition(s, 1, 0, 10)).ToArray();
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Positions = positions, Equity = 1000 + heldCount * 10 } };
        var original = client.Packet;
        var gateway = new Gateway { Scenario = "hold" };
        var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync();
        Assert.Equal("HOLD", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);
        var journal = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope);
        var saved = Assert.Single(journal.ReadAll());
        var checkpoint = Assert.IsType<LiveSnapshot>(saved.ReviewSnapshot);
        var selected = gateway.CandidateSymbols[0].Concat(positions.Select(p => p.Symbol)).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Math.Max(8, heldCount), checkpoint.Quotes.Count);
        Assert.True(selected.SetEquals(checkpoint.Quotes.Select(q => q.Symbol)));
        Assert.Equal(JsonSerializer.Serialize(original.Snapshot with { Quotes = checkpoint.Quotes }), JsonSerializer.Serialize(checkpoint));
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(247, input.ExecutionLimits!.AllowedSymbols.Count);
            Assert.Equal(positions, input.ActualPositions);
        });
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(client.Packet));

        clock.Now = clock.Now.AddHours(1);
        var quotes = client.Packet.ResearchQuotes.Select(q => q with { ObservedAt = clock.Now }).ToArray();
        client.Packet = client.Packet with { ResearchQuotes = quotes,
            Snapshot = client.Packet.Snapshot with { Version = 2, ObservedAt = clock.Now, Quotes = quotes } };
        await coordinator.TickAsync();
        Assert.Equal("WATCHING", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);

        var outside = client.Packet.Limits.AllowedSymbols.First(s => !selected.Contains(s));
        quotes = quotes.Select(q => q.Symbol == outside ? q with { Bid = 11, Ask = 11 } : q).ToArray();
        client.Packet = client.Packet with { ResearchQuotes = quotes, Snapshot = client.Packet.Snapshot with { Quotes = quotes } };
        await coordinator.TickAsync();
        Assert.Equal("WATCHING", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.Single(journal.ReadAll());
        Assert.Equal(JsonSerializer.Serialize(original.Limits), JsonSerializer.Serialize(client.Packet.Limits));
        Assert.Equal(positions, client.Packet.Snapshot.Positions);
        Assert.Empty(client.Intents);
        Assert.Empty(client.Plans);

        var reviewed = gateway.CandidateSymbols[0][0];
        quotes = quotes.Select(q => q.Symbol == reviewed ? q with { Bid = 10.11m, Ask = 10.11m } : q).ToArray();
        client.Packet = client.Packet with { ResearchQuotes = quotes, Snapshot = client.Packet.Snapshot with { Quotes = quotes } };
        await coordinator.TickAsync();
        Assert.Equal("HOLD", coordinator.Status);
        Assert.True(gateway.Inputs.Count > 8);
        Assert.Equal(2, journal.ReadAll().Count);
    }

    [Fact]
    public async Task Dynamic_public_watchlist_is_filtered_to_declared_quoted_markets_with_held_priority()
    {
        var client = DynamicClient();
        var quotes = new[] { new LiveQuote("XRP-THB", 10, 10, clock.Now), new LiveQuote("DOGE-THB", 10, 10, clock.Now) };
        client.Packet = client.Packet with { ResearchQuotes = quotes,
            Snapshot = client.Packet.Snapshot with { Quotes = quotes, Positions = [new("XRP-THB", 1, 0, 10)], Equity = 1010 } };
        var gateway = new Gateway { Scenario = "hold" };
        var research = new Research(clock) { Symbols = ["DOGE-THB", "M02-THB", "UNLISTED-THB"] };
        var coordinator = Coordinator(client, gateway, research: research);
        await coordinator.TickAsync();
        Assert.Equal("HOLD", coordinator.Status);
        Assert.All(gateway.CandidateSymbols, symbols => Assert.Equal(["XRP-THB", "DOGE-THB"], symbols));
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(2, input.Candidates.Count);
            Assert.True(input.Candidates[0].Held);
            Assert.Equal("XRP-THB", Assert.Single(input.ActualPositions!).Symbol);
        });
        Assert.Equal(12, client.Packet.Limits.AllowedSymbols.Count);
        Assert.Empty(client.Intents);
    }

    [Theory]
    [InlineData("XRP-THB")]
    [InlineData("DOGE-THB")]
    public async Task Dynamic_watchlist_can_register_a_plan_only_with_existing_owner_authority(string symbol)
    {
        var client = DynamicClient();
        var gateway = new Gateway { ProposalSymbol = symbol };
        var coordinator = Coordinator(client, gateway, research: new Research(clock) { Symbols = [symbol] });
        await coordinator.TickAsync();
        Assert.Equal("PLAN_REGISTERED", coordinator.Status);
        Assert.Equal(symbol, Assert.Single(client.Plans).Symbol);
        Assert.Empty(client.Intents);
        Assert.Equal(12, client.Packet.Limits.AllowedSymbols.Count);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    public async Task An_existing_large_portfolio_can_register_a_new_authorized_market_from_the_bounded_watchlist(int heldCount)
    {
        var client = DynamicClient(13);
        var positions = client.Packet.Limits.AllowedSymbols.Take(heldCount).Select(s => new LivePosition(s, 1, 0, 10)).ToArray();
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Positions = positions, Equity = 1000 + heldCount * 10 } };
        var original = client.Packet;
        var watchlist = positions.Take(7).Select(p => p.Symbol).Append("XRP-THB").ToArray();
        var gateway = new Gateway { ProposalSymbol = "XRP-THB" };
        var coordinator = Coordinator(client, gateway, continuous: true, research: new Research(clock) { Symbols = watchlist });
        await coordinator.TickAsync();
        Assert.Equal("PLAN_REGISTERED", coordinator.Status);
        Assert.Equal("XRP-THB", Assert.Single(client.Plans).Symbol);
        Assert.All(gateway.CandidateSymbols, symbols => Assert.Equal(watchlist, symbols));
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(8, input.Candidates.Count);
            Assert.Equal(7, input.Candidates.Count(c => c.Held));
            Assert.Equal(positions, input.ActualPositions);
            Assert.Equal(heldCount, input.Portfolio.OpenPositions);
            Assert.Equal(13, input.ExecutionLimits!.AllowedSymbols.Count);
            Assert.Equal(13, input.MaximumOpenPositions);
        });
        var journal = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope);
        var checkpoint = Assert.IsType<LiveSnapshot>(Assert.Single(journal.ReadAll()).ReviewSnapshot);
        Assert.Equal(heldCount + 1, checkpoint.Quotes.Count);
        Assert.Equal(positions, checkpoint.Positions);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(client.Packet));
        Assert.Empty(client.Intents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_large_portfolio_with_no_usable_public_watchlist_keeps_the_held_fallback_advisory(bool undeclared)
    {
        var client = DynamicClient(13);
        var positions = client.Packet.Limits.AllowedSymbols.Take(12).Select(s => new LivePosition(s, 1, 0, 10)).ToArray();
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Positions = positions, Equity = 1120 } };
        var gateway = new Gateway { Scenario = "hold" };
        var coordinator = Coordinator(client, gateway, research: new Research(clock) { Symbols = undeclared ? ["UNLISTED-THB"] : [] });
        await coordinator.TickAsync();
        Assert.Equal("READ_ONLY", coordinator.Status);
        Assert.Equal(new LiveMeetingIssue("PUBLIC_RESEARCH_UNAVAILABLE"), Assert.Single(coordinator.Issues));
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(8, input.Candidates.Count);
            Assert.All(input.Candidates, c => Assert.True(c.Held));
            Assert.Equal(positions, input.ActualPositions);
        });
        Assert.Empty(client.Plans);
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task Dynamic_research_membership_cannot_grant_owner_execution_authority()
    {
        var client = DynamicClient();
        client.Packet = client.Packet with { Limits = client.Packet.Limits with {
            AllowedSymbols = client.Packet.Limits.AllowedSymbols.Where(s => s != "XRP-THB").ToArray(),
            OrderSizing = client.Packet.Limits.OrderSizing.Where(s => s.Symbol != "XRP-THB").ToArray() } };
        var gateway = new Gateway { ProposalSymbol = "XRP-THB" };
        var coordinator = Coordinator(client, gateway, research: new Research(clock) { Symbols = ["XRP-THB"] });
        await coordinator.TickAsync();
        Assert.Equal("INVALID_FINAL", coordinator.Status);
        Assert.All(gateway.Inputs, input => Assert.False(Assert.Single(input.Candidates).ExecutionAllowed));
        Assert.Empty(client.Plans);
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task More_than_eight_actual_holdings_remain_in_input_when_evidence_is_sampled()
    {
        var client = DynamicClient();
        var positions = client.Packet.Limits.AllowedSymbols.Select(s => new LivePosition(s, 1, 0, 10)).ToArray();
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Positions = positions, Equity = 1120 } };
        var gateway = new Gateway { Scenario = "hold" };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("HOLD", coordinator.Status);
        Assert.All(gateway.CandidateSymbols, symbols => Assert.Equal(client.Packet.Limits.AllowedSymbols.Order(StringComparer.Ordinal).Take(8), symbols));
        Assert.All(gateway.Inputs, input =>
        {
            Assert.Equal(8, input.Candidates.Count);
            Assert.All(input.Candidates, c => Assert.True(c.Held));
            Assert.Equal(12, input.Portfolio.OpenPositions);
            Assert.Equal(positions, input.ActualPositions);
        });
    }

    [Fact]
    public async Task Failed_public_research_keeps_the_bounded_fallback_advisory_only()
    {
        var client = DynamicClient();
        var gateway = new Gateway { ProposalSymbol = "DOGE-THB" };
        var research = new Research(clock) { BeforeRead = () => throw new InvalidDataException("fixture unavailable") };
        var coordinator = Coordinator(client, gateway, research: research);
        await coordinator.TickAsync();
        Assert.Equal("READ_ONLY", coordinator.Status);
        Assert.Equal(new LiveMeetingIssue("PUBLIC_RESEARCH_UNAVAILABLE"), Assert.Single(coordinator.Issues));
        Assert.All(gateway.Inputs, input => Assert.Equal(8, input.Candidates.Count));
        Assert.Empty(client.Intents);
        Assert.Empty(client.Plans);
        await coordinator.TickAsync();
        Assert.Equal(new[] { "PUBLIC_RESEARCH_UNAVAILABLE" }, Assert.Single(client.Meetings).Summary!.AdvisoryReasons);
    }

    [Fact]
    public async Task Candidate_with_quote_removed_during_history_refresh_is_omitted_safely()
    {
        var client = DynamicClient();
        void RemoveQuote() => client.Packet = client.Packet with { ResearchQuotes = client.Packet.ResearchQuotes.Where(q => q.Symbol != "DOGE-THB").ToArray(),
            Snapshot = client.Packet.Snapshot with { Quotes = client.Packet.Snapshot.Quotes.Where(q => q.Symbol != "DOGE-THB").ToArray() } };
        var gateway = new Gateway { Scenario = "hold" };
        var coordinator = Coordinator(client, gateway, new Market { AfterRead = RemoveQuote });
        await coordinator.TickAsync();
        Assert.Equal("HOLD", coordinator.Status);
        Assert.All(gateway.Inputs, input => Assert.Equal(7, input.Candidates.Count));
        Assert.All(gateway.CandidateSymbols, symbols => Assert.DoesNotContain("DOGE-THB", symbols));
    }

    [Fact]
    public async Task Selected_quote_removed_after_model_advice_cannot_register_a_plan()
    {
        var client = DynamicClient();
        var gateway = new Gateway { ProposalSymbol = "DOGE-THB", AfterFinal = () =>
            client.Packet = client.Packet with { ResearchQuotes = client.Packet.ResearchQuotes.Where(q => q.Symbol != "DOGE-THB").ToArray(),
                Snapshot = client.Packet.Snapshot with { Quotes = client.Packet.Snapshot.Quotes.Where(q => q.Symbol != "DOGE-THB").ToArray() } } };
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("SNAPSHOT_CHANGED", coordinator.Status);
        Assert.Empty(client.Intents);
        Assert.Empty(client.Plans);
    }

    [Theory]
    [InlineData("allowed")]
    [InlineData("research")]
    [InlineData("quote")]
    [InlineData("sizing")]
    [InlineData("held-quote")]
    [InlineData("undeclared-quote")]
    [InlineData("stale-quote")]
    public async Task Dynamic_market_payload_rejects_injection_and_missing_or_untrusted_quotes_before_models(string defect)
    {
        var client = DynamicClient();
        client.Packet = defect switch
        {
            "allowed" => client.Packet with { Limits = client.Packet.Limits with { AllowedSymbols = ["DOGE-THB\nIGNORE"] } },
            "research" => client.Packet with { Limits = client.Packet.Limits with { ResearchSymbols = [.. client.Packet.Limits.ResearchSymbols, "DOGE-THB\nIGNORE"] } },
            "quote" => client.Packet with { ResearchQuotes = [new("DOGE-THB\nIGNORE", 10, 10, clock.Now)] },
            "sizing" => client.Packet with { Limits = client.Packet.Limits with { OrderSizing = [.. client.Packet.Limits.OrderSizing, new("DOGE-THB\nIGNORE", 1, 1, 1, 1)] } },
            "held-quote" => client.Packet with { ResearchQuotes = [new("DOGE-THB", 10, 10, clock.Now)],
                Snapshot = client.Packet.Snapshot with { Positions = [new("XRP-THB", 1, 0, 10)], Equity = 1010 } },
            "undeclared-quote" => client.Packet with { ResearchQuotes = [new("UNLISTED-THB", 10, 10, clock.Now)] },
            _ => client.Packet with { ResearchQuotes = [new("DOGE-THB", 10, 10, clock.Now.AddSeconds(-31))] }
        };
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway);
        await coordinator.TickAsync();
        Assert.Equal("LIVE_BLOCKED", coordinator.Status);
        Assert.Empty(gateway.Inputs);
        Assert.Empty(client.Intents);
        Assert.Empty(client.Plans);
    }

    [Fact]
    public async Task Continuous_review_blocks_pending_orders()
    {
        var client = new Client();
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { CashReserved = 1 } };
        var gateway = new Gateway();
        var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync();
        Assert.Equal("READ_ONLY", coordinator.Status);
        Assert.Equal(8, gateway.Inputs.Count);
        await coordinator.TickAsync();
        Assert.Equal(new[] { "FUNDS_RESERVED" }, Assert.Single(client.Meetings).Summary!.AdvisoryReasons);
    }

    [Fact]
    public async Task Scout_membership_change_triggers_five_role_review_without_expanding_execution()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        var research = new Research(clock);
        var coordinator = Coordinator(client, gateway, continuous: true, research: research);
        await coordinator.TickAsync();
        Assert.Equal(8, gateway.Inputs.Count);
        Assert.Equal("DOT-THB", Assert.Single(gateway.Inputs[0].Research!.Watchlist).Symbol);
        clock.Now = clock.Now.AddHours(1);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        await coordinator.TickAsync();
        Assert.Equal("WATCHING", coordinator.Status); Assert.Equal(8, gateway.Inputs.Count);
        research.Key = new string('b', 64);
        await coordinator.TickAsync();
        Assert.Equal(13, gateway.Inputs.Count);
        Assert.Equal(new[] { "market-research", "security-analysis", "portfolio-initial", "risk-critic", "portfolio-final" }, gateway.Roles.Skip(8));
        await coordinator.TickAsync();
        Assert.Equal(13, gateway.Inputs.Count);
        Assert.Empty(client.Intents);
        Assert.Equal(["BTC-THB"], client.Packet.Limits.AllowedSymbols);
    }

    [Fact]
    public async Task Scout_symbol_cannot_be_promoted_to_a_financial_proposal()
    {
        var client = new Client(); var gateway = new Gateway { ProposalSymbol = "DOT-THB" };
        var coordinator = Coordinator(client, gateway, continuous: true, research: new Research(clock));
        await coordinator.TickAsync();
        Assert.Equal("INVALID_FINAL", coordinator.Status);
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task Research_before_first_daily_meeting_cannot_submit_an_order()
    {
        clock.Now = clock.Now.AddHours(-2);
        var client = new Client(); var gateway = new Gateway(); var research = new Research(clock);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        var coordinator = Coordinator(client, gateway, continuous: true, research: research);
        await coordinator.TickAsync();
        Assert.Equal(1, research.Calls);
        Assert.Equal("BEFORE_MEETING", coordinator.Status);
        Assert.Empty(gateway.Inputs); Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task Slow_research_claims_the_refreshed_hour_and_not_the_hour_when_it_started()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        var research = new Research(clock);
        var coordinator = Coordinator(client, gateway, continuous: true, research: research);
        await coordinator.TickAsync();
        clock.Now = clock.Now.AddHours(1).AddMinutes(59);
        void RefreshPacket() => client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        RefreshPacket();
        research.Key = new string('b', 64);
        research.BeforeRead = () => { clock.Now = clock.Now.AddMinutes(2); RefreshPacket(); };
        await coordinator.TickAsync();
        var journal = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope);
        var day = DateOnly.FromDateTime(clock.Now.ToOffset(TimeSpan.FromHours(7)).DateTime);
        Assert.NotNull(journal.Read(day, clock.Now.ToOffset(TimeSpan.FromHours(7)).Hour));
        Assert.Null(journal.Read(day, clock.Now.AddHours(-1).ToOffset(TimeSpan.FromHours(7)).Hour));
        Assert.Equal(clock.Now, gateway.Inputs[^1].ObservedAt);
        Assert.Empty(client.Intents);
    }

    private sealed class Research(Clock clock) : ICoinResearchProvider
    {
        public string Key = new string('a', 64);
        public string[] Symbols = ["DOT-THB"];
        public int Calls;
        public Action? BeforeRead;
        public Func<CoinNewsBrief>? ReadNews;
        public Task<CoinResearchBrief> ReadAsync(LiveResearchPacket packet, CancellationToken token)
        {
            Calls++;
            BeforeRead?.Invoke();
            var news = ReadNews?.Invoke();
            return Task.FromResult(new CoinResearchBrief(clock.Now, news?.ChangeKey ?? Key,
                Symbols.Select(s => new CoinScoutCandidate(s, 100, 100.1m, 500000, -2, .1m, 10000, false, false)).ToArray(), [], [], ["ข้อมูลทดสอบเท่านั้น"]) { News = news });
        }
    }

    [Fact]
    public async Task Changed_news_reaches_next_scheduled_review_without_duplicate_meetings_or_authority_changes()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" }; var revision = 0;
        var sourceAt = clock.Now.AddHours(-1);
        var research = new Research(clock) { ReadNews = () =>
        {
            var title = "BTC crypto event " + revision; const string url = "https://www.coindesk.com/markets/btc-event";
            var hash = CoinNewsValidation.ContentHash("CoinDesk", title, url, sourceAt, "PUBLISHED", title);
            CoinNewsItem[] items = [new("news-" + hash, "CoinDesk", "REPORT", title, url, sourceAt, "PUBLISHED", clock.Now, hash, ["BTC-THB"], false, title)];
            CoinNewsCoverage[] coverage = [new("CoinDesk", "CURRENT"), new("Bitkub", "UNAVAILABLE")];
            return new(clock.Now, CoinNewsValidation.ChangeKey(items, coverage), items, coverage);
        } };
        var coordinator = Coordinator(client, gateway, continuous: true, research: research);
        await coordinator.TickAsync(); Assert.Equal(8, gateway.Inputs.Count);
        var initialKey = gateway.Inputs[0].Research!.News!.ChangeKey;
        clock.Now += TimeSpan.FromHours(1); RefreshPacket();
        await coordinator.TickAsync(); Assert.Equal(8, gateway.Inputs.Count); // identical news and prices need no AI rerun
        revision++; clock.Now += TimeSpan.FromMinutes(15); RefreshPacket();
        await coordinator.TickAsync(); Assert.Equal(13, gateway.Inputs.Count);
        Assert.All(gateway.Inputs.Skip(8), input =>
        {
            Assert.True(input.ResearchChanged); Assert.True(input.PortfolioReview);
            Assert.NotEqual(initialKey, input.Research!.News!.ChangeKey);
            Assert.DoesNotContain("advisory only", input.Mandate); // unavailable publisher does not pause otherwise valid trading
            Assert.Equal(JsonSerializer.Serialize(client.Packet.Limits), JsonSerializer.Serialize(input.ExecutionLimits));
        });
        Assert.Contains("market-research", gateway.Roles.Skip(8));
        await coordinator.TickAsync(); Assert.Equal(13, gateway.Inputs.Count);
        Assert.Empty(client.Intents); Assert.Empty(client.Plans);
        var attempts = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll();
        Assert.Equal(initialKey, attempts.Single(a => a.ReviewHour is null).Summary!.Research!.News!.ChangeKey);
        Assert.NotEqual(initialKey, attempts.Single(a => a.ReviewHour is not null).Summary!.Research!.News!.ChangeKey);
        void RefreshPacket() => client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
    }

    private Client DynamicClient(int marketCount = 12)
    {
        var client = new Client();
        string[] symbols = [.. Enumerable.Range(0, marketCount - 2).Select(i => $"M{i:00}-THB"), "XRP-THB", "DOGE-THB"];
        symbols = symbols.Order(StringComparer.Ordinal).ToArray();
        var quotes = symbols.Select(s => new LiveQuote(s, 10, 10, clock.Now)).ToArray();
        client.Packet = client.Packet with { Limits = client.Packet.Limits with { AllowedSymbols = symbols, ResearchSymbols = symbols,
            MaximumOpenPositions = marketCount, OrderSizing = symbols.Select(s => new LiveOrderSizing(s, .01m, .01m, 1, 1)).ToArray() },
            Snapshot = client.Packet.Snapshot with { Quotes = quotes }, ResearchQuotes = quotes };
        return client;
    }

    [Fact]
    public async Task Quarter_hour_reviews_allow_two_changed_rounds_in_one_hour_without_replaying_the_slot()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        client.Packet = client.Packet with { MinimumReviewIntervalMinutes = 15 };
        var coordinator = Coordinator(client, gateway, continuous: true, reviewMinutes: 15);
        await coordinator.TickAsync(); Assert.Equal(8, gateway.Inputs.Count);
        clock.Now = clock.Now.AddMinutes(14); Refresh(1.02m);
        await coordinator.TickAsync(); Assert.Equal(8, gateway.Inputs.Count);
        clock.Now = clock.Now.AddMinutes(1); Refresh(1m);
        await coordinator.TickAsync(); Assert.Equal(11, gateway.Inputs.Count);
        clock.Now = clock.Now.AddMinutes(14); Refresh(1.02m);
        await coordinator.TickAsync(); Assert.Equal(11, gateway.Inputs.Count);
        clock.Now = clock.Now.AddMinutes(1); Refresh(1m);
        await coordinator.TickAsync(); Assert.Equal(14, gateway.Inputs.Count);
        await coordinator.TickAsync(); Assert.Equal(14, gateway.Inputs.Count);
        var attempts = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll();
        Assert.Equal(new[] { 15, 30 }, attempts.Where(a => a.ReviewHour is not null).Select(a => a.ReviewMinute));
        Assert.Empty(client.Intents); Assert.Empty(client.Plans);
        Assert.Equal(clock.Now.AddMinutes(15), coordinator.NextRetryAt);
        void Refresh(decimal factor) => client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with
        { ObservedAt = clock.Now, Quotes = client.Packet.Snapshot.Quotes.Select(q => q with
            { ObservedAt = clock.Now, Bid = q.Bid * factor, Ask = q.Ask * factor }).ToArray() } };
    }

    [Fact]
    public async Task Quarter_hour_with_unchanged_evidence_does_not_repeat_model_work()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        client.Packet = client.Packet with { MinimumReviewIntervalMinutes = 15 };
        var coordinator = Coordinator(client, gateway, continuous: true, reviewMinutes: 15);
        await coordinator.TickAsync();
        clock.Now = clock.Now.AddMinutes(15);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        await coordinator.TickAsync();
        Assert.Equal(8, gateway.Inputs.Count); Assert.Equal("WATCHING", coordinator.Status);
        Assert.Single(new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll());
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task Slow_research_crossing_quarter_boundary_claims_the_refreshed_slot()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" }; var research = new Research(clock);
        client.Packet = client.Packet with { MinimumReviewIntervalMinutes = 15 };
        var coordinator = Coordinator(client, gateway, continuous: true, research: research, reviewMinutes: 15);
        await coordinator.TickAsync();
        clock.Now = clock.Now.AddMinutes(14); Refresh(); research.Key = new string('b', 64);
        research.BeforeRead = () => { clock.Now = clock.Now.AddMinutes(2); Refresh(); };
        await coordinator.TickAsync();
        var review = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll()
            .Single(a => a.ReviewHour is not null);
        Assert.Equal(15, review.ReviewMinute); Assert.Equal(clock.Now, review.ReviewSnapshot!.ObservedAt);
        Assert.Equal(13, gateway.Inputs.Count); Assert.Empty(client.Intents);
        void Refresh() => client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
    }

    [Fact]
    public async Task Older_executor_keeps_hourly_report_keys_until_its_quarter_hour_support_is_installed()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        Assert.Equal(60, client.Packet.MinimumReviewIntervalMinutes);
        var coordinator = Coordinator(client, gateway, continuous: true, reviewMinutes: 15);
        await coordinator.TickAsync();
        clock.Now = clock.Now.AddMinutes(15); Refresh(); await coordinator.TickAsync();
        Assert.Equal(8, gateway.Inputs.Count);
        clock.Now = clock.Now.AddMinutes(45); Refresh(); await coordinator.TickAsync();
        Assert.Equal(11, gateway.Inputs.Count);
        var review = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope).ReadAll()
            .Single(a => a.ReviewHour is not null);
        Assert.Equal(0, review.ReviewMinute); Assert.Empty(client.Intents);
        void Refresh() => client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now, Bid = q.Bid * 1.02m,
                Ask = q.Ask * 1.02m }).ToArray() } };
    }

    [Fact]
    public async Task Saved_report_replay_failure_obeys_retry_backoff_before_replaying_again()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync(); client.SaveFailures = 1;
        await coordinator.TickAsync();
        Assert.Equal("LIVE_UNAVAILABLE", coordinator.Status); Assert.Equal(1, client.SaveCalls);
        await coordinator.TickAsync(); Assert.Equal(1, client.SaveCalls); Assert.Equal(8, gateway.Inputs.Count);
        clock.Now = clock.Now.AddMinutes(5);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        await coordinator.TickAsync(); Assert.Equal(2, client.SaveCalls); Assert.Single(client.Meetings);
        Assert.Empty(client.Intents);
    }

    [Fact]
    public async Task Historical_report_replay_is_bounded_to_one_per_tick_before_today_meeting()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        var journal = new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope);
        foreach (var date in new[] { LiveRiskTests.Now.Date.AddDays(-2), LiveRiskTests.Now.Date.AddDays(-1) })
        {
            var day = DateOnly.FromDateTime(date);
            var id = Guid.NewGuid();
            Assert.True(journal.TryBegin(day, id));
            journal.Complete(day, id, "HOLD");
        }

        var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync();
        Assert.Equal(1, client.SaveCalls);
        Assert.Single(client.Meetings);
        Assert.Contains(journal.ReadAll(), attempt => attempt.BusinessDate == DateOnly.FromDateTime(LiveRiskTests.Now.Date) && attempt.Status == "HOLD");

        await coordinator.TickAsync();
        Assert.Equal(2, client.SaveCalls);
        Assert.Equal(2, client.Meetings.Count);
        Assert.Equal(DateOnly.FromDateTime(LiveRiskTests.Now.Date), client.Meetings[^1].BusinessDate);
        Assert.Empty(client.Intents);
    }

    private LiveCompanyCoordinator Coordinator(Client client, Gateway gateway, Market? market = null, bool continuous = false,
        ICoinResearchProvider? research = null, int reviewMinutes = 60)
    {
        var runner = new AiCompanyMeetingRunner(gateway,
            new JsonAgentDecisionArtifactStore(Path.Combine(root, "artifacts")),
            new(true, "test-model", "medium", "company-v1", "live-v1", 600, 8, 20000, TimeSpan.FromSeconds(30)));
        return new(client, market ?? new Market(), runner,
            new JsonDailyMeetingJournal(Path.Combine(root, "live-meetings.json"), Scope), clock, LiveRiskTests.Account, continuous, research,
            reviewIntervalMinutes: reviewMinutes);
    }

    [Fact]
    public async Task Overnight_portfolio_review_does_not_replace_next_daily_research_meeting()
    {
        var client = new Client(); var gateway = new Gateway { Scenario = "hold" };
        var coordinator = Coordinator(client, gateway, continuous: true);
        await coordinator.TickAsync();
        clock.Now = LiveRiskTests.Now.AddDays(1).AddHours(-2);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 2, ObservedAt = clock.Now,
            CashBalance = 999, Equity = 999, Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        await coordinator.TickAsync();
        Assert.Equal(11, gateway.Inputs.Count);
        Assert.True(gateway.Inputs[^1].PortfolioReview);
        clock.Now = clock.Now.AddHours(2);
        client.Packet = client.Packet with { Snapshot = client.Packet.Snapshot with { Version = 3, ObservedAt = clock.Now,
            Quotes = client.Packet.Snapshot.Quotes.Select(q => q with { ObservedAt = clock.Now }).ToArray() } };
        await coordinator.TickAsync();
        Assert.Equal(19, gateway.Inputs.Count);
        Assert.False(gateway.Inputs[^1].PortfolioReview);
        Assert.Empty(client.Intents);
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
        public List<LiveTradePlanRequest> Plans { get; } = [];
        public List<DailyMeetingAttempt> Meetings { get; } = [];
        public Action? AfterSubmit { get; set; }
        public int ReadFailures { get; set; }
        public Action? BeforeRead { get; set; }
        public int SaveFailures { get; set; }
        public int SaveCalls { get; private set; }
        public Task SaveMeetingAsync(DailyMeetingAttempt attempt, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (SaveFailures-- > 0) return Task.FromException(new IOException("Synthetic report transport failure"));
            Meetings.Add(attempt); return Task.CompletedTask;
        }
        public Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken)
        {
            BeforeRead?.Invoke();
            return ReadFailures-- > 0 ? Task.FromException<LiveResearchPacket>(new IOException("fixture unavailable")) : Task.FromResult(Packet);
        }
        public Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            AfterSubmit?.Invoke();
            return Task.FromResult(new LiveIntentReceipt(intent, LiveOrderState.Acknowledged, []));
        }
        public Task<LiveTradePlanReceipt> RegisterPlanAsync(LiveTradePlanRequest plan, CancellationToken cancellationToken)
        {
            Plans.Add(plan);
            AfterSubmit?.Invoke();
            return Task.FromResult(new LiveTradePlanReceipt(plan.Id, true, []));
        }
    }
    internal sealed class Gateway : ITradingAgentGateway
    {
        public string Scenario { get; init; } = "buy";
        public string Action { get; init; } = "BUY";
        public decimal TargetWeight { get; init; } = .15m;
        public string ProposalSymbol { get; init; } = "BTC-THB";
        public string[]? ProposalSymbols { get; init; }
        public Action? AfterFinal { get; set; }
        public List<AiCompanyMeetingInput> Inputs { get; } = [];
        public List<string> Roles { get; } = [];
        public List<string[]> CandidateSymbols { get; } = [];
        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken)
        {
            if (Scenario == "model-down") throw new AgentModelUnavailableException("fixture");
            using var document = JsonDocument.Parse(request.InputJson);
            var input = document.RootElement.GetProperty("meeting").Deserialize<AiCompanyMeetingInput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            CandidateSymbols.Add(document.RootElement.GetProperty("meeting").GetProperty("candidates").EnumerateArray()
                .Select(c => c.GetProperty("symbol").GetProperty("value").GetString()!).ToArray());
            Inputs.Add(input);
            Roles.Add(request.Role);
            if (request.Role == "portfolio-final") AfterFinal?.Invoke();
            var candidate = input.Candidates.FirstOrDefault(c => c.Symbol.Value == ProposalSymbol) ?? input.Candidates[0];
            var quote = candidate.CurrentQuote!;
            // Deliberately emit an unauthorized proposal even when its sizing is
            // absent, so the test reaches validation instead of crashing the fake.
            var sizing = input.ExecutionLimits!.OrderSizing.FirstOrDefault(s => s.Symbol == ProposalSymbol)
                ?? input.ExecutionLimits.OrderSizing.FirstOrDefault() ?? new(ProposalSymbol, .01m, .01m, 1, 1);
            var amount = Math.Min(TargetWeight * input.Portfolio.Equity, Math.Min(input.Portfolio.Cash,
                input.ExecutionLimits.MaximumOrderNotional ?? input.Portfolio.Equity)) / (1 + input.ExecutionLimits.FeeReserveRate);
            var quantity = decimal.Floor(amount / quote.Ask / sizing.BaseStep) * sizing.BaseStep;
            var tradePlan = Action == "BUY" ? new AiCompanyTradePlan(quote.Ask, quantity,
                quote.Ask * 1.10m, quote.Ask * .95m, input.ObservedAt!.Value.AddHours(4)) : null;
            var output = JsonSerializer.Serialize(new
            {
                role = request.Role, summary = "Review completed market bars", recommendations = new[] { "Review evidence" },
                risks = new[] { "Price changes" }, evidenceIds = input.Candidates.Select(c => c.EvidenceId),
                dataCutoff = input.DataCutoff.ToString("yyyy-MM-dd"), evidenceHash = input.EvidenceHash,
                proposals = request.Role != "portfolio-final" || Scenario == "hold" ? [] : (ProposalSymbols ?? [ProposalSymbol])
                    .Select(symbol => new { action = Action, symbol = Scenario == "invalid-output" ? "INVENTED" : symbol, targetWeight = TargetWeight,
                        confidence = .8m, thesis = "Supplied evidence supports entry", invalidation = "Trend deteriorates", tradePlan }
                    ).ToArray()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(new AgentModelResponse(output, "test-response", request.Model, 100, 50, TimeSpan.Zero));
        }
    }
    internal sealed class Market : IHistoricalMarketEvidenceProvider
    {
        public string Scenario { get; set; } = "normal";
        public string? FailedSymbol { get; init; }
        public string[] FailedSymbols { get; init; } = [];
        public Action? AfterRead { get; init; }
        public Task<IReadOnlyList<HistoricalBar>> GetAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<HistoricalMarketEvidence> GetEvidenceAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken)
        {
            if (symbol.Value == FailedSymbol || FailedSymbols.Contains(symbol.Value)) throw new InvalidDataException("fixture conflicting history");
            var count = Scenario == "missing" ? 10 : 250;
            var bars = Enumerable.Range(0, count).Select(i => new HistoricalBar(end.AddDays(i - count + 1), symbol,
                75000m + i, 76000m + i, 74000m + i, 75500m + i, 1000)).ToArray();
            if (Scenario == "future") bars[^1] = bars[^1] with { Date = end.AddDays(1) };
            AfterRead?.Invoke();
            return Task.FromResult(new HistoricalMarketEvidence(bars, bars, [], Scenario == "synthetic" ? "synthetic-demo" : "bitkub-daily", "THB"));
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
