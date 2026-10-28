using System.Text.Json;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class PolicyCandidateChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new string('a', 64);
        var hash = new string('b', 64);
        var run = new TeamRun(
            Guid.NewGuid(),
            "policy",
            TeamRunStatus.Completed,
            now.AddMinutes(-5),
            now,
            [],
            null,
            account
        );
        var assessment = new ResearchAssessment(
            "research-assessment-v1",
            account,
            run.Id,
            hash,
            now.AddMinutes(-1),
            now.AddMinutes(10),
            ["exact-id"],
            [],
            []
        );
        var parameters = new PolicyCandidateParameters(2, 50, 20, 200, 100, 100, 1.5m, 120, 15, 15, 1, 1, .5m, .5m, 1);
        var candidate = new PolicyCandidate(
            run.Id,
            account,
            hash,
            now.AddMinutes(-1),
            now.AddMinutes(10),
            ["exact-id"],
            "evidence",
            parameters
        );
        var report = new Quantara.Team.Application.ResearchReport(
            "ok",
            null,
            null,
            [],
            assessment,
            null,
            null,
            candidate
        );
        var snapshot =
            $"{{\"evidence\":{{\"policyHash\":\"{hash}\",\"aiPolicy\":{{\"enabled\":true,\"effectiveRulesHash\":\"{hash}\"}},\"tradingStrategy\":{{\"version\":\"adaptive-intraday-v2\"}}}}}}";
        Assert(
            PolicyCandidateValidation.Validate(report, TeamRole.Reviewer, run, account, snapshot, now).PolicyCandidate
                is not null,
            "valid candidate rejected"
        );
        Assert(
            PolicyCandidateValidation
                .Validate(
                    report with
                    {
                        PolicyCandidate = candidate with { EvidenceIds = ["exact-id-extra"] },
                    },
                    TeamRole.Reviewer,
                    run,
                    account,
                    snapshot,
                    now
                )
                .PolicyCandidate
                is null,
            "forged evidence accepted"
        );
        Assert(
            PolicyCandidateValidation
                .Validate(
                    report with
                    {
                        PolicyCandidate = candidate with { AccountKey = new string('c', 64) },
                    },
                    TeamRole.Reviewer,
                    run,
                    account,
                    snapshot,
                    now
                )
                .PolicyCandidate
                is null,
            "wrong account accepted"
        );
        Assert(
            PolicyCandidateValidation
                .Validate(
                    report with
                    {
                        PolicyCandidate = candidate with { ExpiresAt = now.AddHours(1) },
                    },
                    TeamRole.Reviewer,
                    run,
                    account,
                    snapshot,
                    now
                )
                .PolicyCandidate
                is null,
            "long expiry accepted"
        );
        Console.WriteLine("AI policy candidate validation checks passed.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
