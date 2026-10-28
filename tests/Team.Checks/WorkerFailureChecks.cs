using Quantara.Team.Application;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class WorkerFailureChecks
{
    public static async Task Run()
    {
        var originalKey = Environment.GetEnvironmentVariable("CODEX_API_KEY");
        var originalBrokerKey = Environment.GetEnvironmentVariable("BITKUB_API_KEY");
        var originalBrokerSecret = Environment.GetEnvironmentVariable("BITKUB_API_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("BITKUB_API_KEY", "must-not-reach-worker");
            Environment.SetEnvironmentVariable("BITKUB_API_SECRET", "must-not-reach-worker");
            var worker = new CodexWorker(
                NodeRuntime.Resolve(null),
                Path.GetFullPath("tests/Team.Checks/worker-fixture.mjs")
            );

            try
            {
                await worker.Report("reviewer", "classified failure", [], null, CancellationToken.None);
                throw new InvalidOperationException("classified worker failure was accepted");
            }
            catch (CodexWorkerException error)
            {
                Check(error.Code == "worker_response_invalid", "allowlisted worker failure code");
                Check(!error.Message.Contains("sensitive", StringComparison.Ordinal), "safe typed worker message");
            }

            foreach (
                var malformed in new[]
                {
                    "malformed-usage",
                    "cache-over-input",
                    "audit-string",
                    "audit-array",
                    "audit-unknown-fields",
                }
            )
            {
                try
                {
                    await worker.Report("news", "audit metadata failure cases", [], malformed, CancellationToken.None);
                    throw new InvalidOperationException("Malformed audit metadata was accepted");
                }
                catch (CodexWorkerException error)
                {
                    Check(error.Code == "worker_response_invalid", "malformed metadata is classified");
                    Check(error.ResponseFailure == WorkerResponseFailure.AuditMetadata, "malformed metadata stage");
                    Check(error.ModelAudit == worker.ModelSelection("news"), "canonical model lineage is retained");
                    Check(error.Usage is null, "uncertain metadata does not retain usage");
                    Check(!error.Message.Contains("secret", StringComparison.Ordinal), "malformed metadata stays safe");
                }
            }

            try
            {
                await worker.Report("news", "audit metadata failure cases", [], "unknown-code", CancellationToken.None);
                throw new InvalidOperationException("Unknown malformed failure was accepted");
            }
            catch (CodexWorkerException error)
            {
                Check(error.Code == "worker_runtime_failed", "unknown malformed failure is generic");
                Check(
                    error.ResponseFailure is null
                        && error.Usage is null
                        && !error.Message.Contains("secret", StringComparison.Ordinal),
                    "unknown metadata stays generic and safe"
                );
            }

            try
            {
                await worker.Report("news", "audit metadata failure cases", [], "valid", CancellationToken.None);
                throw new InvalidOperationException("Valid failure envelope was accepted");
            }
            catch (CodexWorkerException error)
            {
                Check(error.Code == "worker_response_invalid", "valid failure keeps its classification");
                Check(error.ModelAudit == worker.ModelSelection("news"), "valid failure keeps canonical model lineage");
                Check(
                    error.Usage is { InputTokens: 20, OutputTokens: 4, CachedInputTokens: 1 },
                    "valid failure usage remains available"
                );
            }

            try
            {
                await worker.Report("reviewer", "unknown failure code", [], null, CancellationToken.None);
                throw new InvalidOperationException("unknown worker failure was accepted");
            }
            catch (CodexWorkerException error)
            {
                Check(error.Code == "worker_runtime_failed", "unknown code is generic");
                Check(!error.Message.Contains("secret", StringComparison.Ordinal), "unknown diagnostic is safe");
            }

            Check(
                CodexWorkerException.FromDiagnostic("{\"error\":{\"code\":\"evil\",\"message\":\"secret\"}}").Code
                    == "worker_runtime_failed",
                "malformed code is generic"
            );
            Check(
                CodexWorkerException.FromDiagnostic("not-json").Code == "worker_runtime_failed",
                "malformed diagnostic is generic"
            );
            foreach (
                var code in new[] { "worker_proposal_invalid", "worker_lesson_invalid", "worker_selection_invalid" }
            )
            {
                var error = CodexWorkerException.FromDiagnostic(
                    "{\"error\":{\"code\":\"" + code + "\",\"message\":\"sensitive secret\"}}"
                );
                Check(error.Code == code, "response failure subtype retained");
                Check(!error.Message.Contains("secret", StringComparison.Ordinal), "response subtype is safe");
            }

            var changed = CodexWorkerException.FromDiagnostic(
                "{\"error\":{\"code\":\"worker_evidence_changed\",\"message\":\"secret\"}}"
            );
            Check(changed.Code == "worker_evidence_changed", "evidence change subtype retained");
            Check(
                changed.Message
                    == "Research context changed [worker_evidence_changed]. The account, holdings, trading settings or research window changed during analysis. Stale results were discarded.",
                "evidence change has clear safe message"
            );
            Check(!changed.Message.Contains("secret", StringComparison.Ordinal), "evidence change is safe");
            var generic = new CodexWorkerException("worker_response_invalid");
            var staged = CodexWorkerException.FromDiagnostic(
                "{\"error\":{\"code\":\"worker_response_invalid\",\"message\":\"secret\"},\"responseDiagnostic\":{\"version\":1,\"stage\":\"response_json\",\"responseCharacters\":42}}"
            );
            Check(
                staged.ResponseFailure == WorkerResponseFailure.ResponseJson && staged.ResponseCharacters == 42,
                "bounded parser stage survives process diagnostic"
            );
            Check(
                staged.Message.Contains("response_json", StringComparison.Ordinal)
                    && !staged.Message.Contains("secret", StringComparison.Ordinal),
                "safe stage persists in failure message"
            );
            foreach (var stage in new[] { "secret", "assessment_lineage" })
            {
                var rejected = CodexWorkerException.FromDiagnostic(
                    "{\"error\":{\"code\":\"worker_response_invalid\"},\"responseDiagnostic\":{\"version\":1,\"stage\":\""
                        + stage
                        + "\",\"responseCharacters\":42}}"
                );
                Check(
                    rejected.Code == generic.Code
                        && rejected.ResponseFailure is null
                        && rejected.ResponseCharacters is null,
                    "untrusted worker diagnostic cannot invent a server semantic stage"
                );
            }

            Check(
                new CodexWorkerException("worker_response_invalid", responseCharacters: 100001).ResponseCharacters
                    is null,
                "oversized diagnostic counts stay unknown"
            );
            foreach (
                var (code, stage, category) in new[]
                {
                    ("worker_proposal_invalid", "proposal_content", WorkerResponseFailure.ProposalContent),
                    ("worker_lesson_invalid", "lesson_content", WorkerResponseFailure.LessonContent),
                    ("worker_selection_invalid", "research_selection", WorkerResponseFailure.ResearchSelection),
                }
            )
            {
                var specific = CodexWorkerException.FromDiagnostic(
                    "{\"error\":{\"code\":\""
                        + code
                        + "\"},\"responseDiagnostic\":{\"version\":1,\"stage\":\""
                        + stage
                        + "\",\"responseCharacters\":42}}"
                );
                Check(
                    specific.Code == code
                        && specific.ResponseFailure == category
                        && specific.ResponseCharacters == 42
                        && specific.Message.Contains(stage, StringComparison.Ordinal),
                    "classified parser stage survives process boundary"
                );
            }

            Check(
                generic.Message
                    == "Codex worker failed [worker_response_invalid]. The analysis did not match the required response contract.",
                "legacy invalid-response message is preserved"
            );
            foreach (var reason in Enum.GetValues<WorkerResponseFailure>())
            {
                var detailed = new CodexWorkerException("worker_response_invalid", reason);
                Check(
                    detailed.Code == generic.Code && detailed.ResponseFailure == reason,
                    "safe category preserves failure code"
                );
                Check(
                    detailed.Message.StartsWith(generic.Message, StringComparison.Ordinal),
                    "category augments existing failure"
                );
            }

            Check(
                new CodexWorkerException("worker_response_invalid", (WorkerResponseFailure)999).Message
                    == generic.Message,
                "unknown category is omitted"
            );
            Check(
                new CodexWorkerException("worker_timed_out", WorkerResponseFailure.AssessmentLineage).ResponseFailure
                    is null,
                "categories cannot relabel other failures"
            );

            foreach (var objective in new[] { "mismatched audit success", "mismatched audit failure" })
            {
                try
                {
                    await worker.Report("news", objective, [], null, CancellationToken.None);
                    throw new InvalidOperationException("Mismatched audit was accepted.");
                }
                catch (CodexWorkerException error)
                {
                    Check(error.Code == "worker_response_invalid", "Valid-shaped mismatched audit fails closed.");
                    Check(
                        error.ModelAudit == worker.ModelSelection("news"),
                        "Server selection survives mismatched success/failure envelopes."
                    );
                    Check(
                        error.Usage is null,
                        "Usage from a mismatched lineage is not attributed to the requested model."
                    );
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("BITKUB_API_KEY", originalBrokerKey);
            Environment.SetEnvironmentVariable("BITKUB_API_SECRET", originalBrokerSecret);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Check failed: {name}");
        }
    }
}
