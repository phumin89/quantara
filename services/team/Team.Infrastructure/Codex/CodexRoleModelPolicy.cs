using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class CodexRoleModelPolicy
{
    private static readonly string[] profileFields = ["effort", "model"];

    private readonly Dictionary<string, ResearchModelAudit> selections;

    public CodexRoleModelPolicy(string path, string? overrides)
    {
        try
        {
            if (overrides?.Length > 2000)
            {
                throw new CodexWorkerException("worker_model_policy_invalid");
            }

            using var defaults = JsonDocument.Parse(File.ReadAllText(path));
            using var configured = JsonDocument.Parse(overrides ?? "{}");
            var roles = Enum.GetValues<TeamRole>().Select(role => role.ToString().ToLowerInvariant()).Order().ToArray();
            if (
                defaults.RootElement.ValueKind != JsonValueKind.Object
                || configured.RootElement.ValueKind != JsonValueKind.Object
                || !defaults.RootElement.EnumerateObject().Select(item => item.Name).Order().SequenceEqual(roles)
                || configured.RootElement.EnumerateObject().Select(item => item.Name).Distinct().Count()
                    != configured.RootElement.EnumerateObject().Count()
                || configured.RootElement.EnumerateObject().Any(item => !roles.Contains(item.Name))
            )
            {
                throw new CodexWorkerException("worker_model_policy_invalid");
            }

            var values = new Dictionary<string, ResearchModelAudit>(StringComparer.Ordinal);
            foreach (var role in roles)
            {
                var baseline = ReadProfile(role, defaults.RootElement.GetProperty(role), "default");
                values.Add(
                    role,
                    configured.RootElement.TryGetProperty(role, out var profile)
                        ? ReadProfile(role, profile, "role-override")
                        : baseline
                );
            }

            this.selections = values;
        }
        catch (Exception error)
            when (error
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
            )
        {
            throw new CodexWorkerException("worker_model_policy_invalid");
        }
    }

    public ResearchModelAudit Select(string role) =>
        this.selections.TryGetValue(role, out var selection)
            ? selection
            : throw new CodexWorkerException("worker_model_policy_invalid");

    private static ResearchModelAudit ReadProfile(string role, JsonElement profile, string source)
    {
        if (
            profile.ValueKind != JsonValueKind.Object
            || !profile
                .EnumerateObject()
                .Select(item => item.Name)
                .Order()
                .SequenceEqual(CodexRoleModelPolicy.profileFields)
        )
        {
            throw new CodexWorkerException("worker_model_policy_invalid");
        }

        var model = profile.GetProperty("model").GetString();
        var effort = profile.GetProperty("effort").GetString();
        if (
            model is not ("gpt-6-luna" or "gpt-6.1-sol")
            || effort is not ("low" or "medium" or "high")
            || ((role is "reviewer" or "quant" or "positions") && (model != "gpt-6.1-sol" || effort == "low"))
        )
        {
            throw new CodexWorkerException("worker_model_policy_invalid");
        }

        return new ResearchModelAudit("role-model-v1", model, "openai", effort, source, null, "sdk-not-exposed", null);
    }
}
