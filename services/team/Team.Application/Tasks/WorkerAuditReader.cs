using System.Text.Json;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public static class WorkerAuditReader
{
    private static readonly string[] modelFields =
    [
        "actualModel",
        "actualModelSource",
        "fallback",
        "policyVersion",
        "provider",
        "reasoningEffort",
        "requestedModel",
        "selectionSource",
    ];

    private static readonly string[] usageFields = ["cachedInputTokens", "inputTokens", "outputTokens"];

    public static ResearchModelAudit? Model(JsonElement root)
    {
        if (!root.TryGetProperty("modelAudit", out var audit) || audit.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (audit.ValueKind != JsonValueKind.Object)
        {
            throw new CodexWorkerException("worker_response_invalid");
        }

        var names = audit.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
        if (!names.SequenceEqual(WorkerAuditReader.modelFields))
        {
            throw new CodexWorkerException("worker_response_invalid");
        }

        var version = Text(audit, "policyVersion");
        var model = Text(audit, "requestedModel");
        var provider = Text(audit, "provider");
        var effort = Text(audit, "reasoningEffort");
        var selection = Text(audit, "selectionSource");
        var actualSource = Text(audit, "actualModelSource");
        if (
            version != "role-model-v1"
            || model is not ("gpt-6-luna" or "gpt-6.1-sol")
            || provider != "openai"
            || effort is not ("low" or "medium" or "high")
            || selection is not ("default" or "role-override")
            || actualSource != "sdk-not-exposed"
            || audit.GetProperty("actualModel").ValueKind != JsonValueKind.Null
            || audit.GetProperty("fallback").ValueKind != JsonValueKind.Null
        )
        {
            throw new CodexWorkerException("worker_response_invalid");
        }

        return new ResearchModelAudit(version, model, provider, effort, selection, null, actualSource, null);
    }

    public static ResearchUsage? Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (
            usage.ValueKind != JsonValueKind.Object
            || !usage
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name)
                .SequenceEqual(WorkerAuditReader.usageFields)
            || !usage.GetProperty("inputTokens").TryGetInt64(out var input)
            || !usage.GetProperty("outputTokens").TryGetInt64(out var output)
            || !usage.GetProperty("cachedInputTokens").TryGetInt64(out var cached)
            || input < 0
            || output < 0
            || cached < 0
            || cached > input
        )
        {
            throw new CodexWorkerException("worker_response_invalid");
        }

        return new ResearchUsage(input, output, cached);
    }

    private static string? Text(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
