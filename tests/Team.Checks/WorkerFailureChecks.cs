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
