using Quantara.Team.Application;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class SpecialistWorkerTransportChecks
{
    public static void Run()
    {
        var report = CodexWorker.Decode(
            "{\"output\":\"ok\",\"proposal\":null,\"lesson\":null,\"researchSymbols\":[\"BTC\"],\"assessment\":null,\"usage\":{\"inputTokens\":10,\"outputTokens\":4,\"cachedInputTokens\":3}}",
            "intraday"
        );
        Check(report.ResearchSymbols?.Single() == "BTC", "intraday research symbol accepted");
        Check(report.Usage?.InputTokens == 10 && report.Usage.CachedInputTokens == 3, "usage decoded");
        const string prefix = "{\"output\":\"ok\",\"proposal\":null,\"lesson\":null,\"assessment\":null";
        Check(CodexWorker.Decode(prefix + "}", "news").Usage is null, "Missing usage remains Unknown.");
        foreach (
            var invalid in new[]
            {
                "{\"inputTokens\":-1,\"outputTokens\":4,\"cachedInputTokens\":0}",
                "{\"inputTokens\":10.5,\"outputTokens\":4,\"cachedInputTokens\":0}",
                "{\"inputTokens\":10,\"outputTokens\":4,\"cachedInputTokens\":11}",
                "{\"inputTokens\":10,\"outputTokens\":4}",
                "{\"inputTokens\":10,\"outputTokens\":4,\"cachedInputTokens\":0,\"extra\":0}",
            }
        )
        {
            var rejected = false;
            try
            {
                CodexWorker.Decode(prefix + ",\"usage\":" + invalid + "}", "news");
            }
            catch (CodexWorkerException)
            {
                rejected = true;
            }

            Check(rejected, "Malformed provider usage must be rejected.");
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
