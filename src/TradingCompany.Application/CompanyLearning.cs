using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record CompanyLearningLesson(string ArtifactId, string Quote, string Lesson, string NextPractice);
public sealed record CompanyRoleLearning(string Role, string Status, int ReportsReviewed, CompanyLearningLesson? Lesson);
public sealed record CompanyLearningReport(DateOnly BusinessDate, TradingAccountContext AccountContext,
    IReadOnlyList<CompanyRoleLearning> Roles, int ModelCalls);

public static class CompanyLearningValidation
{
    public static IReadOnlyList<string> Roles { get; } = Array.AsReadOnly(new[]
    { "ceo", "chief-of-staff", "market-research", "security-analysis", "quant-research", "portfolio-initial", "risk-critic", "portfolio-final" });

    public static bool IsValid(CompanyLearningReport? value, TradingAccountContext? context, DateOnly date) =>
        value is not null && context is { Mode: TradingMode.Live } && TradingAccountContext.IsValid(context) &&
        value.AccountContext == context && value.BusinessDate != default && value.BusinessDate <= date &&
        value.BusinessDate >= date.AddDays(-7) && value.ModelCalls is 0 or 1 && value.Roles is { Count: 8 } &&
        value.Roles.Select(r => r?.Role).SequenceEqual(Roles) && value.Roles.All(IsValidRole);

    public static bool IsValidRole(CompanyRoleLearning? r) => r is not null &&
            (r.Status == "ACCEPTED" && r.ReportsReviewed == 1 && r.Lesson is { } l &&
                Guid.TryParse(l.ArtifactId, out var id) && id != Guid.Empty && Text(l.Quote, 200) && l.Quote.Length >= 20 &&
                Text(l.Lesson, 250) && Text(l.NextPractice, 250) ||
             r.Status is "NO_HISTORY" or "INVALID" && r.ReportsReviewed == 0 && r.Lesson is null);

    private static bool Text(string? value, int bound) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= bound && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
}
