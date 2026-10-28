using System.Text.Json;
using System.Text.Json.Nodes;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchGoalContextChecks
{
    public static async Task Run()
    {
        var fixture = await new FakePortfolioSnapshotReader { TradingMode = "stopped" }.Read(CancellationToken.None);
        string[] goals =
        [
            """
                {"id":"fixture-goal-a","targetThb":25000.12500,"deadlineExclusive":"2027-02-01T00:00:00+07:00","includesDeposits":true,"timeZone":"Asia/Bangkok"}
                """,
            """
                {"id":"fixture-goal-b","targetThb":3.1415e4,"deadlineExclusive":"2027-03-12T18:45:00-04:00","includesDeposits":false,"timeZone":"America/New_York"}
                """,
            """
                {"id":"fixture-expired-goal","targetThb":1500.00,"deadlineExclusive":"2020-01-01T00:00:00Z","includesDeposits":true,"timeZone":"UTC"}
                """,
        ];
        TeamRole[] roles =
        [
            TeamRole.Ceo,
            TeamRole.Analyst,
            TeamRole.Reviewer,
            TeamRole.Intraday,
            TeamRole.Swing,
            TeamRole.News,
            TeamRole.Positions,
            TeamRole.Quant,
        ];
        for (var index = 0; index < goals.Length; index++)
        {
            var account = new string((char)('a' + index), 64);
            foreach (var role in roles)
            {
                var snapshot = Snapshot(fixture, account);
                snapshot["objective"] = JsonNode.Parse(goals[index]);
                CheckContext(role, account, snapshot, true);
            }
        }

        var unknownAccount = new string('f', 64);
        CheckContext(TeamRole.Reviewer, unknownAccount, Snapshot(fixture, unknownAccount), false);
        foreach (var json in new[] { "null", "[]", "true", "1", "\"unknown\"", "{}" })
        {
            var snapshot = Snapshot(fixture, unknownAccount);
            snapshot["objective"] = JsonNode.Parse(json);
            CheckContext(TeamRole.Reviewer, unknownAccount, snapshot, false);
        }

        (string Field, string? Json)[] invalidFields =
        [
            ("id", null),
            ("id", "\" \""),
            ("id", "null"),
            ("targetThb", null),
            ("targetThb", "0"),
            ("targetThb", "-1"),
            ("targetThb", "\"25000\""),
            ("targetThb", "1.00000000000000000000000000001"),
            ("targetThb", "1e100"),
            ("targetThb", "1e-100"),
            ("deadlineExclusive", null),
            ("deadlineExclusive", "\"not-a-date\""),
            ("deadlineExclusive", "\"0001-01-01T00:00:00+00:00\""),
            ("deadlineExclusive", "123"),
            ("includesDeposits", null),
            ("includesDeposits", "\"true\""),
            ("includesDeposits", "null"),
            ("timeZone", null),
            ("timeZone", "\" \""),
            ("timeZone", "null"),
        ];
        foreach (var invalid in invalidFields)
        {
            var goal = JsonNode.Parse(goals[0])?.AsObject() ?? throw new InvalidDataException("Fixture goal missing.");
            if (invalid.Json is null)
            {
                goal.Remove(invalid.Field);
            }
            else
            {
                goal[invalid.Field] = JsonNode.Parse(invalid.Json);
            }

            var snapshot = Snapshot(fixture, unknownAccount);
            snapshot["objective"] = goal;
            CheckContext(TeamRole.Reviewer, unknownAccount, snapshot, false);
        }

        Console.WriteLine(
            "Research goals: exact account evidence, role scope, decimal/date text, expired metadata and unknown malformed goals."
        );
    }

    private static JsonObject Snapshot(string fixture, string account)
    {
        var snapshot =
            JsonNode.Parse(fixture)?.AsObject() ?? throw new InvalidDataException("Fixture snapshot missing.");
        snapshot["accountKey"] = account;
        snapshot["policyHash"] = new string('e', 64);
        var wallet =
            snapshot["walletEvidence"]?.AsObject() ?? throw new InvalidDataException("Fixture wallet missing.");
        wallet["accountKey"] = account;
        return snapshot;
    }

    private static void CheckContext(TeamRole role, string account, JsonObject snapshot, bool validGoal)
    {
        var roleName = role.ToString().ToLowerInvariant();
        var evidence = TeamResearchScope.Project(role, snapshot.ToJsonString());
        using var source = JsonDocument.Parse(evidence);
        var now = DateTimeOffset.UtcNow;
        ResearchTask? task = role is TeamRole.Ceo or TeamRole.Analyst
            ? null
            : new ResearchTask(
                role == TeamRole.Reviewer ? TeamRole.Intraday : role,
                new string('d', 64),
                new string('e', 64),
                now,
                now.AddMinutes(20)
            );
        var current = new TeamRun(
            Guid.NewGuid(),
            "Account goal fixture",
            TeamRunStatus.Running,
            now,
            now,
            [],
            null,
            account,
            Task: task
        );
        using var context = JsonDocument.Parse(
            TeamResearchContext.Build(roleName, current.Objective, [], evidence, current, [], null)
        );
        var root = context.RootElement;
        var actual = root.GetProperty("currentGoal");
        if (validGoal)
        {
            var expected = source.RootElement.GetProperty("objective");
            Check(
                actual.ValueKind == JsonValueKind.Object
                    && JsonNode.DeepEquals(JsonNode.Parse(actual.GetRawText()), JsonNode.Parse(expected.GetRawText())),
                "Each role retains the complete goal from its own account evidence."
            );
            Check(
                actual.GetProperty("targetThb").GetRawText() == expected.GetProperty("targetThb").GetRawText()
                    && actual.GetProperty("deadlineExclusive").GetString()
                        == expected.GetProperty("deadlineExclusive").GetString(),
                "Numeric scale/exponent and deadline offset text remain exact, including expired goals."
            );
        }
        else
        {
            Check(actual.ValueKind == JsonValueKind.Null, "Unavailable or malformed goals have no hardcoded fallback.");
        }

        Check(
            JsonNode.DeepEquals(
                JsonNode.Parse(root.GetProperty("evidence").GetRawText()),
                JsonNode.Parse(source.RootElement.GetRawText())
            ),
            "Goal projection does not rewrite the role's supplied evidence."
        );
        Check(
            root.GetProperty("evidence").GetProperty("accountKey").GetString() == account
                && root.GetProperty("assessmentContext").GetProperty("accountKey").GetString() == account
                && !root.GetProperty("assessmentContext").GetProperty("executionAuthority").GetBoolean(),
            "The exact account and read-only assessment authority remain unchanged."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
