using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ModelAuditChecks
{
    public static async Task Run()
    {
        var path = Path.Combine("services", "ai-worker", "CodexModelPolicy.json");
        var policy = new CodexRoleModelPolicy(path, null);
        foreach (var role in Enum.GetValues<TeamRole>())
        {
            var audit = policy.Select(role.ToString().ToLowerInvariant());
            Check(
                audit.ActualModel is null && audit.Fallback is null,
                "Unknown returned model remains unknown; no fallback."
            );
            Check(audit.Provider == "openai" && audit.SelectionSource == "default", "Explicit server profile.");
        }

        Check(
            policy.Select("reviewer").ReasoningEffort == "medium",
            "Reviewer starts within bounded latency hypothesis."
        );
        var overridden = new CodexRoleModelPolicy(path, "{\"news\":{\"model\":\"gpt-6.1-sol\",\"effort\":\"medium\"}}");
        Check(overridden.Select("news").SelectionSource == "role-override", "Override provenance.");
        foreach (
            var invalid in new[]
            {
                "null",
                "[]",
                "{",
                "{\"news\":{\"model\":\"unsupported\",\"effort\":\"low\"}}",
                "{\"reviewer\":{\"model\":\"gpt-6-luna\",\"effort\":\"low\"}}",
                "{\"unknown\":{\"model\":\"gpt-6.1-sol\",\"effort\":\"medium\"}}",
            }
        )
        {
            try
            {
                _ = new CodexRoleModelPolicy(path, invalid);
                throw new InvalidOperationException("Invalid policy was accepted.");
            }
            catch (CodexWorkerException error)
            {
                Check(error.Code == "worker_model_policy_invalid", "Invalid policy fails before worker spawn.");
            }
        }

        var legacy = JsonSerializer.Deserialize<TeamStep>(
            "{\"Role\":0,\"Status\":0,\"Output\":\"legacy\",\"Error\":null}"
        );
        Check(legacy?.ModelAudit is null, "Legacy records never gain inferred model metadata.");
        var selected = policy.Select("news");
        var step = new TeamStep(TeamRole.News, TeamRunStatus.Running, null, null, ModelAudit: selected);
        Check(
            JsonSerializer.Deserialize<TeamStep>(JsonSerializer.Serialize(step))?.ModelAudit == selected,
            "Running selection survives retained JSON."
        );
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var envelope = JsonSerializer.Serialize(
            new
            {
                output = "Synthetic",
                proposal = (object?)null,
                lesson = (object?)null,
                modelAudit = selected,
                usage = new ResearchUsage(20, 4, 1),
            },
            jsonOptions
        );
        var report = CodexWorker.Decode(envelope, "news");
        Check(report.ModelAudit == selected && report.Usage?.OutputTokens == 4, "Worker audit decode.");
        var failure = CodexWorkerException.FromDiagnostic(
            JsonSerializer.Serialize(
                new
                {
                    error = new { code = "worker_response_invalid" },
                    modelAudit = selected,
                    usage = new ResearchUsage(20, 4, 1),
                },
                jsonOptions
            )
        );
        Check(
            failure.ModelAudit == selected && failure.Usage?.InputTokens == 20,
            "Failed contract retains known request and usage."
        );
        foreach (
            var invalid in new[]
            {
                envelope.Replace("\"actualModel\":null", "\"actualModel\":\"invented\"", StringComparison.Ordinal),
                envelope.Replace("\"provider\":\"openai\"", "\"provider\":\"untrusted\"", StringComparison.Ordinal),
                envelope.Replace("\"cachedInputTokens\":1", "\"cachedInputTokens\":21", StringComparison.Ordinal),
            }
        )
        {
            try
            {
                _ = CodexWorker.Decode(invalid, "news");
                throw new InvalidOperationException("Invalid audit was accepted.");
            }
            catch (CodexWorkerException error)
            {
                Check(error.Code == "worker_response_invalid", "Invalid audit fails closed.");
            }
        }

        await RetainedSelection(policy);
        Console.WriteLine(
            "Model audit: explicit profiles, overrides, legacy unknown, retained running/failure metadata and invalid provenance passed offline."
        );
    }

    private static async Task RetainedSelection(CodexRoleModelPolicy policy)
    {
        var store = new FakeTeamRunStore();
        async Task<ResearchReport> Reject(
            string role,
            IReadOnlyList<(string Role, string Output)> previous,
            CancellationToken cancellationToken
        )
        {
            var running = await store.Latest(cancellationToken);
            Check(
                running
                    ?.Steps.Single(step =>
                        string.Equals(step.Role.ToString(), role, StringComparison.OrdinalIgnoreCase)
                    )
                    .ModelAudit == policy.Select(role),
                "Server selection is durably stored before worker starts."
            );
            throw new CodexWorkerException("worker_response_invalid", modelAudit: policy.Select("news"));
        }

        var worker = new FakeCodexWorker(Reject) { SelectModel = role => policy.Select(role) };
        using var coordinator = new TeamCoordinator(
            store,
            worker,
            new FakePortfolioSnapshotReader { TradingMode = "stopped" }
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await coordinator.StartAsync(timeout.Token);
        await coordinator.Initialization;
        try
        {
            var run = await coordinator.Start("Synthetic model lineage", timeout.Token);
            var failed = await store.WaitFor(run.Id, item => item.Status == TeamRunStatus.Failed, timeout.Token);
            Check(
                failed.Steps.Single(step => step.Role == TeamRole.Ceo).ModelAudit == policy.Select("ceo"),
                "Failed worker cannot overwrite pinned selection with another role profile."
            );
            Check(
                failed.Steps.Single(step => step.Role == TeamRole.Ceo).Usage is null,
                "Unknown failed usage is not zero."
            );
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
