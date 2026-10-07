using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record TradingAccountContext(TradingMode Mode, string AccountId)
{
    public static bool IsValid(TradingAccountContext? context) => context is null ||
        (Enum.IsDefined(context.Mode) && !string.IsNullOrWhiteSpace(context.AccountId) &&
         context.AccountId.Length <= 100 && !context.AccountId.Any(char.IsControl) &&
         (context.Mode != TradingMode.Live || (Guid.TryParseExact(context.AccountId, "N", out var id) &&
             id != Guid.Empty && context.AccountId == id.ToString("N"))));
}

public sealed record AiCompanyCandidateEvidence(
    string EvidenceId,
    Symbol Symbol,
    DateOnly DataCutoff,
    decimal LastPrice,
    bool Held,
    decimal HeldQuantity,
    decimal UnrealizedPnl,
    decimal ForecastScore,
    ForecastDirection ForecastDirection,
    StrategyKind Strategy,
    bool StrategyPromoted,
    decimal OutOfSampleReturn,
    decimal ValidatedDrawdown)
{
    public bool ExecutionAllowed { get; init; } = true;
    public LiveQuote? CurrentQuote { get; init; }
}

public sealed record AiCompanyMeetingInput(
    DateOnly DataCutoff,
    string EvidenceHash,
    PortfolioSnapshot Portfolio,
    IReadOnlyList<AiCompanyCandidateEvidence> Candidates,
    string Mandate,
    decimal MaximumPositionWeight,
    int MaximumOpenPositions)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TradingAccountContext? AccountContext { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool PortfolioReview { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ObservedAt { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousConclusion { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CoinResearchBrief? Research { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ResearchChanged { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CompanyLearningReport? Learning { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool LearningChanged { get; init; }
}

public sealed record AiCompanyProposal(
    PortfolioAction Action,
    Symbol Symbol,
    decimal TargetWeight,
    decimal Confidence,
    string Thesis,
    string Invalidation);

public sealed record AiCompanyAgentOutput(
    string Role,
    string Summary,
    ImmutableArray<string> Recommendations,
    ImmutableArray<string> Risks,
    ImmutableArray<string> EvidenceIds,
    ImmutableArray<AiCompanyProposal> Proposals,
    DateOnly DataCutoff,
    string EvidenceHash);

public sealed record AiCompanyOutputValidation(
    bool IsValid,
    AiCompanyAgentOutput? Output,
    ImmutableArray<string> Violations);

public sealed record AiCompanyMeetingResult(
    string Status,
    IReadOnlyList<AiCompanyProposal> Proposals,
    IReadOnlyList<AgentDecisionArtifact> Artifacts,
    int ModelCalls,
    int InputTokens,
    int OutputTokens,
    int CacheHits,
    string Model)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TradingAccountContext? AccountContext { get; init; }
}

public static class AiCompanyContract
{
    private static readonly HashSet<string> OutputFields = new(StringComparer.Ordinal)
    {
        "role", "summary", "recommendations", "risks", "evidenceIds", "proposals", "dataCutoff", "evidenceHash"
    };

    private static readonly HashSet<string> ProposalFields = new(StringComparer.Ordinal)
    {
        "action", "symbol", "targetWeight", "confidence", "thesis", "invalidation"
    };

    public const string OutputJsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["role", "summary", "recommendations", "risks", "evidenceIds", "proposals", "dataCutoff", "evidenceHash"],
          "properties": {
            "role": { "type": "string", "minLength": 1 },
            "summary": { "type": "string", "minLength": 1 },
            "recommendations": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "risks": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "evidenceIds": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "proposals": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["action", "symbol", "targetWeight", "confidence", "thesis", "invalidation"],
                "properties": {
                  "action": { "type": "string", "enum": ["HOLD", "BUY", "TRIM", "SELL"] },
                  "symbol": { "type": "string", "minLength": 1 },
                  "targetWeight": { "type": "number", "minimum": 0, "maximum": 1 },
                  "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                  "thesis": { "type": "string", "minLength": 1 },
                  "invalidation": { "type": "string", "minLength": 1 }
                }
              }
            },
            "dataCutoff": { "type": "string", "format": "date" },
            "evidenceHash": { "type": "string", "minLength": 1 }
          }
        }
        """;

    public static AiCompanyOutputValidation ParseAndValidate(
        AiCompanyMeetingInput input,
        string expectedRole,
        string outputJson,
        bool allowProposals)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRole);
        var violations = ImmutableArray.CreateBuilder<string>();
        try
        {
            using var document = JsonDocument.Parse(outputJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Failure("MALFORMED_JSON");
            ValidateFields(root, OutputFields, violations);
            var role = RequiredString(root, "role");
            var summary = RequiredString(root, "summary");
            var recommendations = RequiredStrings(root, "recommendations");
            var risks = RequiredStrings(root, "risks");
            var evidenceIds = RequiredStrings(root, "evidenceIds");
            var proposals = ParseProposals(root, violations);
            var cutoff = RequiredDate(root, "dataCutoff");
            var evidenceHash = RequiredString(root, "evidenceHash");

            if (!string.Equals(role, expectedRole, StringComparison.Ordinal)) violations.Add("ROLE_MISMATCH");
            if (cutoff != input.DataCutoff) violations.Add("CUTOFF_MISMATCH");
            if (!string.Equals(evidenceHash, input.EvidenceHash, StringComparison.Ordinal)) violations.Add("EVIDENCE_HASH_MISMATCH");
            var allowedIds = input.Candidates.Select(item => item.EvidenceId).ToHashSet(StringComparer.Ordinal);
            if (input.Research is { } research)
            {
                foreach (var source in research.Sources) allowedIds.Add(source.Id);
                foreach (var candidate in research.Watchlist) allowedIds.Add(candidate.EvidenceId);
            }
            if (evidenceIds.Any(id => !allowedIds.Contains(id))) violations.Add("UNKNOWN_EVIDENCE_ID");
            if (!allowProposals && proposals.Length > 0) violations.Add("UNAUTHORIZED_PROPOSAL");
            var symbols = input.Candidates.Select(item => item.Symbol).ToHashSet();
            if (proposals.Any(proposal => !symbols.Contains(proposal.Symbol))) violations.Add("UNKNOWN_SYMBOL");
            if (proposals.GroupBy(proposal => proposal.Symbol).Any(group => group.Count() > 1)) violations.Add("DUPLICATE_SYMBOL");
            if (proposals.Count(proposal => proposal.Action != PortfolioAction.Hold) > input.MaximumOpenPositions)
                violations.Add("TOO_MANY_PROPOSALS");
            if (proposals.Any(proposal => proposal.TargetWeight > input.MaximumPositionWeight))
                violations.Add("POSITION_WEIGHT_EXCEEDED");

            if (violations.Count > 0) return new(false, null, violations.ToImmutable());
            return new(true, new(role, summary, recommendations, risks, evidenceIds, proposals, cutoff, evidenceHash), []);
        }
        catch (JsonException)
        {
            return Failure("MALFORMED_JSON");
        }
        catch (ContractException exception)
        {
            return Failure(exception.Code);
        }

        AiCompanyOutputValidation Failure(string code) => new(false, null, [code]);
    }

    private static ImmutableArray<AiCompanyProposal> ParseProposals(
        JsonElement root,
        ImmutableArray<string>.Builder violations)
    {
        var property = Required(root, "proposals");
        if (property.ValueKind != JsonValueKind.Array) throw new ContractException("INVALID_PROPOSALS");
        var result = ImmutableArray.CreateBuilder<AiCompanyProposal>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new ContractException("INVALID_PROPOSAL");
            ValidateFields(item, ProposalFields, violations);
            var action = RequiredString(item, "action") switch
            {
                "HOLD" => PortfolioAction.Hold,
                "BUY" => PortfolioAction.Buy,
                "TRIM" => PortfolioAction.Trim,
                "SELL" => PortfolioAction.Sell,
                _ => throw new ContractException("UNKNOWN_ACTION")
            };
            var symbol = new Symbol(RequiredString(item, "symbol"));
            var targetWeight = RequiredDecimal(item, "targetWeight");
            var confidence = RequiredDecimal(item, "confidence");
            if (targetWeight is < 0m or > 1m) violations.Add("INVALID_TARGET_WEIGHT");
            if (confidence is < 0m or > 1m) violations.Add("INVALID_CONFIDENCE");
            result.Add(new(action, symbol, targetWeight, confidence,
                RequiredString(item, "thesis"), RequiredString(item, "invalidation")));
        }
        return result.ToImmutable();
    }

    private static void ValidateFields(JsonElement element, HashSet<string> allowed, ImmutableArray<string>.Builder violations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) violations.Add("UNKNOWN_FIELD");
            if (!seen.Add(property.Name)) violations.Add("DUPLICATE_FIELD");
        }
    }

    private static JsonElement Required(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value : throw new ContractException("MISSING_FIELD");

    private static string RequiredString(JsonElement root, string name)
    {
        var value = Required(root, name);
        return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new ContractException("INVALID_STRING");
    }

    private static decimal RequiredDecimal(JsonElement root, string name)
    {
        var value = Required(root, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result)
            ? result
            : throw new ContractException("INVALID_NUMBER");
    }

    private static ImmutableArray<string> RequiredStrings(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.Array) throw new ContractException("INVALID_ARRAY");
        var result = value.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())
                ? item.GetString()!
                : throw new ContractException("INVALID_ARRAY_ITEM")).ToImmutableArray();
        if (result.Length != result.Distinct(StringComparer.Ordinal).Count()) throw new ContractException("DUPLICATE_ARRAY_ITEM");
        return result;
    }

    private static DateOnly RequiredDate(JsonElement root, string name)
        => DateOnly.TryParseExact(RequiredString(root, name), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value)
            ? value
            : throw new ContractException("INVALID_DATE");

    private sealed class ContractException(string code) : Exception
    {
        public string Code { get; } = code;
    }
}
