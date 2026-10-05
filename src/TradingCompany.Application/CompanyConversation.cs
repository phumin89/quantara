using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace TradingCompany.Application;

public sealed record ConversationProposal(string Action, string Symbol, decimal TargetWeight, decimal Confidence, string Thesis, string Invalidation);
public sealed record ConversationMessage(Guid Id, string Role, DateTimeOffset CreatedAt, string Summary,
    IReadOnlyList<string> Recommendations, IReadOnlyList<string> Risks, IReadOnlyList<ConversationProposal> Proposals, string EvidenceStatus);
public sealed record CompanyConversation(DateOnly BusinessDate, string? Status, IReadOnlyList<ConversationMessage> Messages, DateTimeOffset UpdatedAt, bool ExecutionVerified = false);
public sealed record ConversationDates(IReadOnlyList<DateOnly> Dates, DateTimeOffset UpdatedAt);

// Read projection only: no authority to create decisions, approve proposals or alter accounts.
public static class CompanyConversationProjection
{
    private static readonly HashSet<string> Roles = ["ceo", "chief-of-staff", "market-research", "security-analysis", "quant-research", "portfolio-initial", "risk-critic", "portfolio-final"];
    public static DateOnly BusinessDate(AgentDecisionArtifact artifact) => DateOnly.FromDateTime(artifact.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public static IReadOnlyList<(DateOnly Date, ConversationMessage Message)> PaperMessages(ForwardPaperAccount account)
    {
        ForwardPaperLedger.Validate(account);
        var result = new List<(DateOnly, ConversationMessage)>();
        foreach (var entry in account.Events.Where(e => e.Order is not null || e.Fill is not null))
        {
            var order = entry.Order ?? account.Events.Single(e => e.Order?.Id == entry.Fill!.OrderId).Order!;
            var quantity = order.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var role = entry.Fill?.Status == "RISK_REJECTED" ? "risk-engine" : "paper-execution";
            var summary = entry.Fill is null ? $"รับคำสั่งจำลอง {order.Symbol} จำนวน {quantity} แล้ว รอราคาใหม่และการตรวจความเสี่ยง ยังไม่ใช่ผลซื้อขาย" :
                entry.Fill.Status == "RISK_REJECTED" ? $"RiskEngine ปฏิเสธคำสั่งจำลอง {order.Symbol}: {string.Join(", ", entry.Fill.Violations)}" :
                $"บันทึก{(order.Side == TradingCompany.Domain.OrderSide.Buy ? "ซื้อ" : "ขาย")}จำลอง {order.Symbol} จำนวน {quantity} สำเร็จ · ไม่ใช่เงินจริง";
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"sandbox-report:{entry.Kind}:{order.Id}"))[..16]);
            result.Add((DateOnly.FromDateTime(entry.At.ToOffset(TimeSpan.FromHours(7)).DateTime),
                new(id, role, entry.At, summary, [], entry.Fill?.Violations ?? [], [], "accepted")));
        }
        return result;
    }

    public static ConversationMessage Project(AgentDecisionArtifact artifact, DailyMeetingAttempt? meeting)
    {
        using var document = JsonDocument.Parse(artifact.OutputJson);
        var output = document.RootElement;
        if (output.ValueKind != JsonValueKind.Object || !Roles.Contains(artifact.Key.Role) || Text(output, "role") != artifact.Key.Role)
            throw new InvalidDataException("Report role mismatch.");
        var accepted = meeting is { Status: "COMPLETED", Summary: not null } &&
            meeting.Summary.Reports.Any(r => r.ArtifactId == artifact.ArtifactId.ToString() && r.Role == artifact.Key.Role &&
                r.EvidenceHash == artifact.Key.EvidenceHash && r.DataCutoff == artifact.Key.DataCutoff && r.Summary == Text(output, "summary"));
        var proposals = new List<ConversationProposal>();
        if (output.TryGetProperty("proposals", out var values))
        {
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 32) throw new InvalidDataException("Invalid proposals.");
            foreach (var p in values.EnumerateArray())
            {
                var action = Text(p, "action");
                if (action is not ("HOLD" or "BUY" or "TRIM" or "SELL")) throw new InvalidDataException("Invalid proposal action.");
                var weight = p.GetProperty("targetWeight").GetDecimal();
                var confidence = p.GetProperty("confidence").GetDecimal();
                if (weight is < 0 or > 1 || confidence is < 0 or > 1) throw new InvalidDataException("Invalid proposal limits.");
                proposals.Add(new(action, Text(p, "symbol"), weight, confidence, Text(p, "thesis"), Text(p, "invalidation")));
            }
        }
        return new(artifact.ArtifactId, artifact.Key.Role, artifact.CreatedAt, Text(output, "summary"), Strings(output, "recommendations"),
            Strings(output, "risks"), proposals, accepted ? "accepted" : "saved-report");
    }

    private static string Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString()) || text.GetString()!.Length > 20000)
            throw new InvalidDataException("Invalid report text.");
        return text.GetString()!;
    }
    private static IReadOnlyList<string> Strings(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var list)) return [];
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 32) throw new InvalidDataException("Invalid report list.");
        return list.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 and <= 20000 } text
            ? text : throw new InvalidDataException("Invalid report list item.")).ToArray();
    }
}
