using System.Text.Json;

namespace Quantara.Team.Application;

public sealed class CodexWorkerException : InvalidOperationException
{
    public CodexWorkerException(string code)
        : base(CreateMessage(code))
    {
        this.Code = Normalize(code);
    }

    public string Code { get; }

    private static string CreateMessage(string code)
    {
        var normalized = Normalize(code);
        return normalized == "worker_evidence_changed"
            ? "Research context changed [worker_evidence_changed]. " + Describe(code)
            : "Codex worker failed [" + normalized + "]. " + Describe(code);
    }

    public static CodexWorkerException FromDiagnostic(string diagnostic)
    {
        try
        {
            using var document = JsonDocument.Parse(diagnostic);
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
            )
            {
                return new CodexWorkerException(code.GetString() ?? "worker_runtime_failed");
            }
        }
        catch (JsonException)
        {
            // Only allowlisted codes cross the process boundary; raw diagnostics stay private.
        }

        return new CodexWorkerException("worker_runtime_failed");
    }

    private static string Normalize(string code) =>
        code switch
        {
            "worker_input_invalid"
            or "worker_input_too_large"
            or "worker_auth_unavailable"
            or "worker_authentication_failed"
            or "worker_rate_limited"
            or "worker_network_failed"
            or "worker_timed_out"
            or "worker_response_invalid"
            or "worker_proposal_invalid"
            or "worker_lesson_invalid"
            or "worker_selection_invalid"
            or "worker_response_rejected"
            or "worker_unsupported_tool"
            or "worker_evidence_changed" => code,
            _ => "worker_runtime_failed",
        };

    private static string Describe(string code) =>
        Normalize(code) switch
        {
            "worker_input_invalid" => "The research request is invalid.",
            "worker_input_too_large" => "The research context exceeds the worker budget.",
            "worker_auth_unavailable" => "Codex authentication is not configured.",
            "worker_authentication_failed" => "Codex authentication failed.",
            "worker_rate_limited" => "Codex usage or rate limit reached.",
            "worker_network_failed" => "Codex could not be reached.",
            "worker_timed_out" => "The analysis exceeded its time limit.",
            "worker_response_invalid" => "The analysis did not match the required response contract.",
            "worker_proposal_invalid" => "Reviewer returned an invalid proposal. No order was created.",
            "worker_lesson_invalid" => "Reviewer returned an invalid learning lesson. No order was created.",
            "worker_selection_invalid" => "The analysis returned an invalid market selection.",
            "worker_response_rejected" => "The model declined the research request.",
            "worker_unsupported_tool" => "The analysis attempted an unsupported tool.",
            "worker_evidence_changed" =>
                "The account, holdings, trading settings or research window changed during analysis. Stale results were discarded.",
            _ => "The worker runtime failed. Check the server configuration.",
        };
}
