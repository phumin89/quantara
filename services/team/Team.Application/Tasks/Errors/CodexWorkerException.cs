using System.Text.Json;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed class CodexWorkerException : InvalidOperationException
{
    private static readonly string[] diagnosticFields = ["responseCharacters", "stage", "version"];

    public CodexWorkerException(
        string code,
        WorkerResponseFailure? responseFailure = null,
        ResearchModelAudit? modelAudit = null,
        ResearchUsage? usage = null,
        int? responseCharacters = null
    )
        : base(CreateMessage(code, responseFailure, responseCharacters))
    {
        this.Code = Normalize(code);
        this.ModelAudit = modelAudit;
        this.Usage = usage;
        this.ResponseCharacters = responseCharacters is >= 0 and <= 100000 ? responseCharacters : null;
        this.ResponseFailure = AllowsResponseFailure(this.Code, responseFailure) ? responseFailure : null;
    }

    public string Code { get; }

    public ResearchModelAudit? ModelAudit { get; }

    public ResearchUsage? Usage { get; }

    public WorkerResponseFailure? ResponseFailure { get; }

    public int? ResponseCharacters { get; }

    private static string CreateMessage(string code, WorkerResponseFailure? responseFailure, int? responseCharacters)
    {
        var normalized = Normalize(code);
        var message =
            normalized == "worker_evidence_changed"
                ? "Research context changed [worker_evidence_changed]. " + Describe(code)
                : "Codex worker failed [" + normalized + "]. " + Describe(code);
        var reason = AllowsResponseFailure(normalized, responseFailure)
            ? responseFailure switch
            {
                WorkerResponseFailure.AssessmentLineage => "assessment_lineage",
                WorkerResponseFailure.AssessmentLifetime => "assessment_lifetime",
                WorkerResponseFailure.AssessmentBounds => "assessment_bounds",
                WorkerResponseFailure.EvidenceReferences => "evidence_references",
                WorkerResponseFailure.PositionCoverage => "position_coverage",
                WorkerResponseFailure.PositionContent => "position_content",
                WorkerResponseFailure.OpportunityContent => "opportunity_content",
                WorkerResponseFailure.OpportunityDuplicate => "opportunity_duplicate",
                WorkerResponseFailure.OpportunitySymbol => "opportunity_symbol",
                WorkerResponseFailure.OpportunityText => "opportunity_text",
                WorkerResponseFailure.OpportunityReviewTime => "opportunity_review_time",
                WorkerResponseFailure.ResponseJson => "response_json",
                WorkerResponseFailure.ResponseShape => "response_shape",
                WorkerResponseFailure.ReportContent => "report_content",
                WorkerResponseFailure.ProposalContent => "proposal_content",
                WorkerResponseFailure.LessonContent => "lesson_content",
                WorkerResponseFailure.ResearchSelection => "research_selection",
                WorkerResponseFailure.AssessmentShape => "assessment_shape",
                WorkerResponseFailure.AuditMetadata => "audit_metadata",
                _ => null,
            }
            : null;
        message = reason is null ? message : message + " Validation: " + reason + ".";
        return responseCharacters is >= 0 and <= 100000
            ? message + " Response characters: " + responseCharacters.Value + "."
            : message;
    }

    public static CodexWorkerException FromDiagnostic(string diagnostic)
    {
        var normalizedCode = "worker_runtime_failed";
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
                normalizedCode = Normalize(code.GetString() ?? "worker_runtime_failed");
                var (responseFailure, responseCharacters) = ResponseDiagnostic(document.RootElement);
                return new CodexWorkerException(
                    normalizedCode,
                    responseFailure,
                    modelAudit: WorkerAuditReader.Model(document.RootElement),
                    usage: WorkerAuditReader.Usage(document.RootElement),
                    responseCharacters: responseCharacters
                );
            }
        }
        catch (Exception error)
            when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // Only allowlisted codes cross the process boundary; raw diagnostics stay private.
            if (normalizedCode != "worker_runtime_failed")
            {
                // An invalid audit cannot attribute usage to a model, even if one field parsed successfully.
                return new CodexWorkerException("worker_response_invalid", WorkerResponseFailure.AuditMetadata);
            }
        }

        return new CodexWorkerException("worker_runtime_failed");
    }

    private static (WorkerResponseFailure? Failure, int? Characters) ResponseDiagnostic(JsonElement root)
    {
        if (
            !root.TryGetProperty("responseDiagnostic", out var diagnostic)
            || diagnostic.ValueKind != JsonValueKind.Object
            || !diagnostic
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name)
                .SequenceEqual(CodexWorkerException.diagnosticFields)
            || diagnostic.GetProperty("version").ValueKind != JsonValueKind.Number
            || !diagnostic.GetProperty("version").TryGetInt32(out var version)
            || version != 1
            || diagnostic.GetProperty("stage").ValueKind != JsonValueKind.String
        )
        {
            return (null, null);
        }

        WorkerResponseFailure? failure = diagnostic.GetProperty("stage").GetString() switch
        {
            "response_json" => WorkerResponseFailure.ResponseJson,
            "response_shape" => WorkerResponseFailure.ResponseShape,
            "report_content" => WorkerResponseFailure.ReportContent,
            "proposal_content" => WorkerResponseFailure.ProposalContent,
            "lesson_content" => WorkerResponseFailure.LessonContent,
            "research_selection" => WorkerResponseFailure.ResearchSelection,
            "assessment_shape" => WorkerResponseFailure.AssessmentShape,
            _ => null,
        };
        var length = diagnostic.GetProperty("responseCharacters");
        int? characters =
            length.ValueKind == JsonValueKind.Number && length.TryGetInt32(out var count) && count is >= 0 and <= 100000
                ? count
                : null;
        return failure is null ? (null, null) : (failure, characters);
    }

    private static bool AllowsResponseFailure(string code, WorkerResponseFailure? failure) =>
        code switch
        {
            "worker_response_invalid" => failure is { } value && Enum.IsDefined(value),
            "worker_proposal_invalid" => failure == WorkerResponseFailure.ProposalContent,
            "worker_lesson_invalid" => failure == WorkerResponseFailure.LessonContent,
            "worker_selection_invalid" => failure == WorkerResponseFailure.ResearchSelection,
            _ => false,
        };

    private static string Normalize(string code) =>
        code switch
        {
            "worker_input_invalid"
            or "worker_model_policy_invalid"
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
            "worker_model_policy_invalid" => "The configured role model policy is invalid. No fallback was attempted.",
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
