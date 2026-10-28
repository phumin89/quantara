using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Company;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure.Risk;

namespace Portfolio.Checks;

public static class CompanySimulationChecks
{
    public static async Task Run()
    {
        var directory = Directory.CreateTempSubdirectory("quantara-company-paper-").FullName;
        try
        {
            var at = CompanySimulationFixture.At;
            var rules = CompanySimulationFixture.Rules;
            var source = CompanySimulationFixture.Account;
            var journal = new CompanyCapitalEvidenceStore(Path.Combine(directory, "capital.json"), [source]);
            var request = CompanySimulationFixture.Request("losing-cycle", at.AddMinutes(-1), at, true);
            await journal.Import(
                CompanySimulationFixture.Import("opening", source, request.CapitalFrom, at, 1000, 1000),
                0,
                CancellationToken.None
            );
            var evidence = await journal.Read(CancellationToken.None);
            var report = await journal.Report(request.CapitalFrom, at, CancellationToken.None);
            var result = await CompanyPaperCycle.Run(
                request,
                report,
                evidence,
                rules,
                null,
                at.AddSeconds(1),
                CancellationToken.None
            );
            var paper = result.State?.Paper ?? throw new InvalidOperationException("Expected Paper cycle state.");
            CompanySimulationFixture.Check(
                result.Status == "completed" && paper.Fills.Count == 2 && paper.Positions.Single().Quantity == 0,
                "Admitted capital flows through existing strategy, allocation and observed-liquidity Paper fills."
            );
            CompanySimulationFixture.Check(
                result.RealizedNetThb < 0 && result.ExecutionFeesThb > 0 && result.NetPaperChangeThb < 0,
                "The end-to-end stop-loss reports a loss after both fees rather than success from funding."
            );
            CompanySimulationFixture.Check(
                result.Roles.Count == 6
                    && !result.VerifiedIncome
                    && !result.CanUseForLiveDecisions
                    && result.State is { Mode: TradingMode.Paper, Mandate: null }
                    && result.State.Live.Fills.Count == 0,
                "All company responsibilities retain evidence and never acquire live authority or verified income."
            );
            var repeated = await CompanyPaperCycle.Run(
                request,
                report,
                evidence,
                rules,
                null,
                at.AddSeconds(1),
                CancellationToken.None
            );
            CompanySimulationFixture.Check(
                JsonSerializer.Serialize(result) == JsonSerializer.Serialize(repeated),
                "Identical offline inputs produce identical fill identities and outcomes."
            );

            foreach (
                var invalid in new[]
                {
                    request with
                    {
                        Frames = [new(at, [])],
                    },
                    request with
                    {
                        Frames =
                        [
                            new(at, [CompanySimulationFixture.Book(at) with { ObservedAt = at.AddSeconds(-91) }]),
                        ],
                    },
                    request with
                    {
                        Frames =
                        [
                            new(
                                at,
                                [
                                    CompanySimulationFixture.Book(at) with
                                    {
                                        Bars =
                                        [
                                            .. CompanySimulationFixture.Book(at).Bars,
                                            new(at.ToUnixTimeSeconds(), 110, 111, 109, 110, 2),
                                        ],
                                    },
                                ]
                            ),
                        ],
                    },
                }
            )
            {
                var blocked = await CompanyPaperCycle.Run(
                    invalid,
                    report,
                    evidence,
                    rules,
                    null,
                    at,
                    CancellationToken.None
                );
                CompanySimulationFixture.Check(
                    blocked.Status == "blocked" && blocked.State is null && blocked.RealizedNetThb is null,
                    "Missing, stale or future market evidence blocks before any simulated execution."
                );
            }

            CompanySimulationFixture.Check(
                (
                    await CompanyPaperCycle.Run(
                        request,
                        report,
                        evidence,
                        rules,
                        null,
                        at.AddSeconds(91),
                        CancellationToken.None
                    )
                ).Status == "blocked",
                "Stale capital cannot be relabelled as ready."
            );
            CompanySimulationFixture.Check(
                (await CompanyPaperCycle.Run(request, null, evidence, rules, null, at, CancellationToken.None)).Status
                    == "blocked",
                "A missing capital report cannot initialize a company cycle."
            );

            var nextAt = at.AddMinutes(5);
            var funding = new CapitalCashFlow(
                "monthly-funding",
                "THB",
                500,
                0,
                nextAt.AddSeconds(-1),
                CashFlowKind.Deposit,
                500
            );
            await journal.Import(
                CompanySimulationFixture.Import("deposit", source, at, nextAt, 1000, 1500, [funding]),
                1,
                CancellationToken.None
            );
            var nextRequest = CompanySimulationFixture.Request("after-deposit", at, nextAt);
            var funded = await CompanyPaperCycle.Run(
                nextRequest,
                await journal.Report(at, nextAt, CancellationToken.None),
                await journal.Read(CancellationToken.None),
                rules,
                result,
                nextAt,
                CancellationToken.None
            );
            CompanySimulationFixture.Check(
                funded.Status == "completed"
                    && funded.State?.Paper.Fills.Count == 2
                    && funded.State.Paper.CashThb == paper.CashThb + 500
                    && funded.AppliedContributionsThb == 500
                    && funded.NetPaperChangeThb == 0
                    && funded.State.Paper.FundingThb == 1500,
                "A later deposit adds capital once while retaining the prior loss and contribution-adjusted result."
            );
            CompanySimulationFixture.Check(
                funded.RulesHash == result.RulesHash
                    && funded.State?.Rules.DailyLossThb == 1
                    && funded.State.Decisions.Any(decision => decision.Reason == "Daily loss or unknown valuation"),
                "Deposit and missed stretch objective cannot reset the daily-loss guard or increase risk."
            );
            Console.WriteLine(
                "Company Paper core: admitted evidence -> six recorded responsibilities -> fixed allocation -> real Paper fill/fees -> losing review; deterministic identities, missing/stale/future guards and deposit-without-risk-escalation passed."
            );
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                File.Delete(file);
            }

            Directory.Delete(directory);
        }
    }
}
