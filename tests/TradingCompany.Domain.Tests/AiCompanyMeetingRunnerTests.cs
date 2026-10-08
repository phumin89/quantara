using System.Text.Json;
using TradingCompany.AgentRuntime;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class AiCompanyMeetingRunnerTests
{
    private static readonly string[] Roles =
    [
        "ceo", "chief-of-staff", "market-research", "security-analysis",
        "quant-research", "portfolio-initial", "risk-critic", "portfolio-final"
    ];

    [Fact]
    public async Task Meeting_runs_eight_expert_roles_in_order_with_separate_contexts()
    {
        var input = Input();
        var gateway = new QueueGateway(Roles.Select(role => Output(
            input,
            role,
            role == "portfolio-final" ? "BUY" : null)));
        var runner = Runner(gateway);

        var result = await runner.RunAsync(input, default);

        Assert.Equal(Roles, gateway.Requests.Select(request => request.Role));
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(8, result.ModelCalls);
        Assert.Equal(8, result.Artifacts.Count);
        var proposal = Assert.Single(result.Proposals);
        Assert.Equal(new Symbol("NVDA"), proposal.Symbol);
        Assert.Equal(PortfolioAction.Buy, proposal.Action);
        Assert.Equal(.19m, proposal.TargetWeight);
        Assert.All(gateway.Requests, request =>
        {
            Assert.Contains("cannot execute orders", request.SystemInstructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cannot override RiskEngine", request.SystemInstructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(input.EvidenceHash, request.InputJson);
            Assert.Contains("2026-08-29", request.InputJson);
        });
        Assert.DoesNotContain("Market view", gateway.Requests[0].InputJson);
        Assert.Contains("Market view", gateway.Requests[^1].InputJson);
    }

    [Fact]
    public async Task Live_company_receives_funded_net_profit_objective_without_financial_authority()
    {
        var input = Input() with { AccountContext = new(TradingMode.Live, new string('1', 32)) };
        var gateway = new QueueGateway(Roles.Select(role => Output(input, role)));
        var result = await Runner(gateway).RunAsync(input, default);
        Assert.Equal("COMPLETED", result.Status);
        Assert.All(gateway.Requests, request =>
        {
            Assert.Contains("net profit after fees, spread and slippage", request.SystemInstructions);
            Assert.Contains("Do not manufacture confidence, force a trade, or raise the owner's limits", request.SystemInstructions);
            Assert.Contains("cannot override RiskEngine", request.SystemInstructions);
            Assert.Contains("delegates portfolio planning", request.SystemInstructions);
            Assert.Contains("Never claim execution or fills", request.SystemInstructions);
        });
    }

    [Fact]
    public async Task Missing_hosted_model_fails_closed_without_proposals()
    {
        var gateway = new QueueGateway([], new AgentModelUnavailableException("missing"));
        var runner = Runner(gateway);

        var result = await runner.RunAsync(Input(), default);

        Assert.Equal("MODEL_UNAVAILABLE", result.Status);
        Assert.Empty(result.Proposals);
        Assert.Equal(1, result.ModelCalls);
    }

    [Fact]
    public async Task Empty_model_configuration_fails_closed_before_gateway_call()
    {
        var gateway = new QueueGateway([]);
        var runner = new AiCompanyMeetingRunner(
            gateway,
            new MemoryArtifactStore(),
            new AiCompanyRuntimeOptions(
                true, "", "medium", "company-v1", "aggressive-paper-v1",
                600, 8, 20_000, TimeSpan.FromSeconds(30)));

        var result = await runner.RunAsync(Input(), default);

        Assert.Equal("MODEL_UNAVAILABLE", result.Status);
        Assert.Empty(result.Proposals);
        Assert.Empty(gateway.Requests);
        Assert.Equal(0, result.ModelCalls);
    }

    [Fact]
    public async Task Exhausted_api_credit_fails_closed_with_truthful_status()
    {
        var runner = Runner(new QueueGateway([], new AgentQuotaUnavailableException("quota")));

        var result = await runner.RunAsync(Input(), default);

        Assert.Equal("CREDITS_UNAVAILABLE", result.Status);
        Assert.Empty(result.Proposals);
        Assert.Equal(1, result.ModelCalls);
    }

    [Fact]
    public async Task Final_proposal_for_unknown_symbol_fails_closed()
    {
        var input = Input();
        var outputs = Roles.Select(role => Output(
            input,
            role,
            role == "portfolio-final" ? "BUY" : null,
            role == "portfolio-final" ? "INVENTED" : "NVDA"));
        var runner = Runner(new QueueGateway(outputs));

        var result = await runner.RunAsync(input, default);

        Assert.Equal("INVALID_FINAL", result.Status);
        Assert.Empty(result.Proposals);
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("mandate")]
    [InlineData("limits")]
    [InlineData("live")]
    [InlineData("live-account")]
    public async Task Changed_meeting_context_cannot_reuse_old_agent_decisions(string change)
    {
        var original = Input();
        var changed = change switch
        {
            "cash" => original with { Portfolio = original.Portfolio with { Cash = 12000m, Equity = 12000m } },
            "mandate" => original with { Mandate = "Preserve cash; do not propose new entries." },
            "live" => original with { AccountContext = new(TradingMode.Live, "11111111111111111111111111111111") },
            "live-account" => original with { AccountContext = new(TradingMode.Live, "22222222222222222222222222222222") },
            _ => original with { MaximumPositionWeight = .10m }
        };
        var gateway = new QueueGateway(Roles.Select(role => Output(original, role))
            .Concat(Roles.Select(role => Output(changed, role))));
        var runner = Runner(gateway);
        Assert.Equal("COMPLETED", (await runner.RunAsync(original, default)).Status);
        var result = await runner.RunAsync(changed, default);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(8, result.ModelCalls);
        Assert.Equal(0, result.CacheHits);
    }

    [Fact]
    public async Task Live_meeting_result_and_events_remain_bound_to_the_requested_account()
    {
        var input = Input() with { AccountContext = new(TradingMode.Live, "11111111111111111111111111111111") };
        var events = new CapturedEvents();
        var result = await Runner(new QueueGateway(Roles.Select(role => Output(input, role))), events).RunAsync(input, default);
        Assert.Equal(input.AccountContext, result.AccountContext);
        Assert.NotEmpty(events.Items);
        Assert.All(events.Items, item => Assert.Equal(input.AccountContext, item.AccountContext));
    }

    [Theory]
    [InlineData(TradingMode.Live, "")]
    [InlineData(TradingMode.Live, "00000000000000000000000000000000")]
    [InlineData(TradingMode.Live, "paper-default")]
    [InlineData((TradingMode)99, "11111111111111111111111111111111")]
    public async Task Invalid_account_scope_never_spends_model_quota(TradingMode mode, string account)
    {
        var input = Input() with { AccountContext = new(mode, account) };
        var result = await Runner(new QueueGateway(Roles.Select(role => Output(input, role)))).RunAsync(input, default);
        Assert.Equal("INVALID_ACCOUNT_CONTEXT", result.Status);
        Assert.Equal(0, result.ModelCalls);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Identical_meeting_context_reuses_validated_artifacts_without_model_calls()
    {
        var input = Input();
        var runner = Runner(new QueueGateway(Roles.Select(role => Output(input, role))));
        Assert.Equal("COMPLETED", (await runner.RunAsync(input, default)).Status);
        var replay = await runner.RunAsync(input, default);
        Assert.Equal("COMPLETED", replay.Status);
        Assert.Equal(0, replay.ModelCalls);
        Assert.Equal(8, replay.CacheHits);
    }

    [Fact]
    public async Task Office_events_follow_validated_role_work_in_sequence()
    {
        var input = Input();
        var events = new CapturedEvents();
        var runner = Runner(new QueueGateway(Roles.Select(role => Output(input, role))), events);
        Assert.Equal("COMPLETED", (await runner.RunAsync(input, default)).Status);
        Assert.Equal(Roles, events.Items.Where(e => e.Type == "AgentStartedTask")
            .Select(e => ((AgentRuntimeEventPayload)e.Payload).Role));
        Assert.Equal(Roles, events.Items.Where(e => e.Type == "AgentCompletedTask")
            .Select(e => ((AgentRuntimeEventPayload)e.Payload).Role));
        Assert.Equal(new[] { "ceo", "chief-of-staff", "news", "technical", "quant", "portfolio-manager", "risk", "portfolio-manager" },
            events.Items.Where(e => e.Type == "AgentStartedTask").Select(e => ((AgentRuntimeEventPayload)e.Payload).AgentId));
        Assert.True(events.Items.FindIndex(e => e.Type == "AgentCompletedTask") <
            events.Items.FindIndex(e => e.Type == "AgentStartedTask" && ((AgentRuntimeEventPayload)e.Payload).AgentId == "chief-of-staff"));
    }

    [Fact]
    public async Task Prompt_json_preserves_thai_while_escaping_html_quotes_and_controls()
    {
        var input = Input() with { Mandate = "ทดสอบ <tag> \"quote\" และบรรทัด\nใหม่" };
        var thai = "สรุปก่อนหน้า";
        var gateway = new QueueGateway(Roles.Select(role => Output(input, role, summary: thai)));

        var result = await Runner(gateway).RunAsync(input, default);

        Assert.Equal("COMPLETED", result.Status);
        Assert.Contains("ทดสอบ", gateway.Requests[0].InputJson);
        Assert.Contains("สรุปก่อนหน้า", gateway.Requests[1].InputJson);
        Assert.Contains("\\u003Ctag\\u003E", gateway.Requests[0].InputJson);
        Assert.Contains("\\u0022quote\\u0022", gateway.Requests[0].InputJson);
        Assert.Contains("\\n", gateway.Requests[0].InputJson);
        using var first = JsonDocument.Parse(gateway.Requests[0].InputJson);
        using var second = JsonDocument.Parse(gateway.Requests[1].InputJson);
        Assert.Equal(input.Mandate, first.RootElement.GetProperty("meeting").GetProperty("mandate").GetString());
        Assert.Equal(thai, second.RootElement.GetProperty("priorArtifacts")[0].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Daily_memory_is_role_scoped_rechecked_and_included_in_decision_cache()
    {
        var scope = new TradingAccountContext(TradingMode.Live, "11111111111111111111111111111111");
        var day = new DateOnly(2026, 8, 29);
        var memory = new CompanyLearningReport(day, scope, Roles.Select(role => new CompanyRoleLearning(role, "ACCEPTED", 1,
            new(Guid.NewGuid().ToString(), "Saved historical report quotation", role + " lesson", role + " practice"))).ToArray(), 1);
        var input = Input() with { AccountContext = scope, ObservedAt = new DateTimeOffset(2026, 8, 29, 5, 0, 0, TimeSpan.Zero),
            PortfolioReview = true, LearningChanged = true, Learning = memory };
        var gateway = new QueueGateway(Roles.Select(r => Output(input, r)).Concat(Roles.Select(r => Output(input, r))));
        var runner = Runner(gateway);
        Assert.Equal("COMPLETED", (await runner.RunAsync(input, default)).Status);
        Assert.Equal(Roles, gateway.Requests.Select(r => r.Role));
        foreach (var request in gateway.Requests)
        {
            using var json = JsonDocument.Parse(request.InputJson);
            Assert.Equal(request.Role, json.RootElement.GetProperty("roleMemory").GetProperty("role").GetString());
            Assert.False(json.RootElement.GetProperty("meeting").TryGetProperty("learning", out _));
            Assert.Contains("cannot grant permissions", request.SystemInstructions);
        }
        var newRoles = memory.Roles.Select(r => r with { Lesson = r.Lesson! with { NextPractice = r.Role + " revised practice" } }).ToArray();
        var changed = await runner.RunAsync(input with { Learning = memory with { Roles = newRoles } }, default);
        Assert.Equal(8, changed.ModelCalls); Assert.Equal(0, changed.CacheHits);
        var wrongScope = memory with { AccountContext = new(TradingMode.Live, "22222222222222222222222222222222") };
        Assert.Equal("INVALID_LEARNING", (await runner.RunAsync(input with { Learning = wrongScope }, default)).Status);
        Assert.Equal(16, gateway.Requests.Count);
    }

    private sealed class CapturedEvents : IRealtimeEventPublisher
    {
        public List<RealtimeEvent> Items { get; } = [];
        public Task PublishAsync(RealtimeEvent message, CancellationToken cancellationToken) { Items.Add(message); return Task.CompletedTask; }
    }

    private static AiCompanyMeetingRunner Runner(ITradingAgentGateway gateway, IRealtimeEventPublisher? events = null) => new(
        gateway,
        new MemoryArtifactStore(),
        new AiCompanyRuntimeOptions(
            Enabled: true,
            Model: "model-x",
            ReasoningEffort: "medium",
            PromptVersion: "company-v1",
            MandateVersion: "aggressive-paper-v1",
            MaxOutputTokens: 600,
            MaxModelCallsPerMeeting: 8,
            MaxTotalTokensPerMeeting: 20_000,
            Timeout: TimeSpan.FromSeconds(30)), events: events);

    private static AiCompanyMeetingInput Input()
    {
        var cutoff = new DateOnly(2026, 8, 29);
        return new(
            cutoff,
            "evidence-hash",
            new PortfolioSnapshot(100_000m, 100_000m, 0m, 0m, 0m, 0),
            [new(
                "NVDA:2026-08-29",
                new Symbol("NVDA"),
                cutoff,
                5_000m,
                false,
                0m,
                0m,
                82m,
                ForecastDirection.Bullish,
                StrategyKind.TrendRegimeTrailing,
                true,
                .18m,
                .08m)],
            "Aggressive paper trading; no leverage; preserve solvency.",
            .20m,
            4);
    }

    private static string Output(
        AiCompanyMeetingInput input,
        string role,
        string? action = null,
        string symbol = "NVDA",
        string summary = "")
    {
        summary = string.IsNullOrEmpty(summary) ? (role == "market-research" ? "Market view" : "Role view") : summary;
        var proposals = action is null
            ? "[]"
            : $$"""[{"action":"{{action}}","symbol":"{{symbol}}","targetWeight":0.19,"confidence":0.82,"thesis":"Trend and evidence support entry.","invalidation":"Trend breaks."}]""";
        return $$"""
            {"role":"{{role}}","summary":"{{summary}}","recommendations":["Review supplied evidence"],"risks":["Volatility"],"evidenceIds":["NVDA:2026-08-29"],"proposals":{{proposals}},"dataCutoff":"{{input.DataCutoff:yyyy-MM-dd}}","evidenceHash":"{{input.EvidenceHash}}"}
            """;
    }

    private sealed class QueueGateway(IEnumerable<string> outputs, Exception? failure = null) : ITradingAgentGateway
    {
        private readonly Queue<string> outputs = new(outputs);
        public List<AgentModelRequest> Requests { get; } = [];

        public Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (failure is not null) return Task.FromException<AgentModelResponse>(failure);
            return Task.FromResult(new AgentModelResponse(
                outputs.Dequeue(), Guid.NewGuid().ToString("N"), request.Model, 100, 50, TimeSpan.FromMilliseconds(10)));
        }
    }

    private sealed class MemoryArtifactStore : IAgentDecisionArtifactStore
    {
        private readonly List<AgentDecisionArtifact> items = [];

        public Task<AgentDecisionArtifact> AppendAsync(AgentDecisionArtifact artifact, CancellationToken cancellationToken)
        {
            items.Add(artifact);
            return Task.FromResult(artifact);
        }

        public Task<AgentDecisionArtifact?> FindEarliestAsync(AgentArtifactKey key, CancellationToken cancellationToken)
            => Task.FromResult(items.FirstOrDefault(item => item.Key == key));
    }
}

public sealed class AiCompanyProposalSizerTests
{
    [Fact]
    public void Buy_sizes_from_current_equity_and_target_weight()
    {
        var proposal = Proposal(PortfolioAction.Buy, .19m);

        var intent = AiCompanyProposalSizer.Size(proposal, null, 5_000m, 100_000m);

        Assert.NotNull(intent);
        Assert.Equal(OrderSide.Buy, intent.Side);
        Assert.Equal(3m, intent.Quantity);
    }

    [Fact]
    public void Sell_closes_only_an_owned_position()
    {
        var position = new PaperPosition(new Symbol("NVDA"), 7m, 4_000m, 5_000m, 35_000m, 7_000m);

        var intent = AiCompanyProposalSizer.Size(Proposal(PortfolioAction.Sell, 0m), position, 5_000m, 100_000m);

        Assert.NotNull(intent);
        Assert.Equal(OrderSide.Sell, intent.Side);
        Assert.Equal(7m, intent.Quantity);
        Assert.Null(AiCompanyProposalSizer.Size(Proposal(PortfolioAction.Sell, 0m), null, 5_000m, 100_000m));
    }

    [Fact]
    public void Trim_reduces_to_target_weight_without_increasing_position()
    {
        var position = new PaperPosition(new Symbol("NVDA"), 7m, 4_000m, 5_000m, 35_000m, 7_000m);

        var intent = AiCompanyProposalSizer.Size(Proposal(PortfolioAction.Trim, .20m), position, 5_000m, 100_000m);

        Assert.NotNull(intent);
        Assert.Equal(OrderSide.Sell, intent.Side);
        Assert.Equal(3m, intent.Quantity);
        Assert.Null(AiCompanyProposalSizer.Size(Proposal(PortfolioAction.Trim, .40m), position, 5_000m, 100_000m));
    }

    [Fact]
    public void Hold_never_creates_an_order()
        => Assert.Null(AiCompanyProposalSizer.Size(Proposal(PortfolioAction.Hold, 0m), null, 5_000m, 100_000m));

    private static AiCompanyProposal Proposal(PortfolioAction action, decimal weight)
        => new(action, new Symbol("NVDA"), weight, .8m, "Evidence supports action.", "Evidence invalidates action.");
}
