using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchAssessmentChecks
{
    public static async Task Run()
    {
        ResearchEvidenceDigestChecks.Run();
        var account = new string('a', 64);
        var created = DateTimeOffset.UtcNow;
        var run = new TeamRun(Guid.NewGuid(), "assessment", TeamRunStatus.Running, created, created, [], null, account);
        var evidence = """
            {"policyHash":"policy-1","positions":[{"symbol":"ETH","quantity":1}],"walletEvidence":{"balances":[{"symbol":"BTC","quantity":1},{"symbol":"THB","quantity":5000}]},"marketResearch":{"available":true,"scan":{"symbols":["BTC_THB","ETH_THB"]}}}
            """;
        var news = "{\"events\":[{\"eventId\":\"n1\",\"revision\":2}]}";
        using var evidenceDocument = JsonDocument.Parse(evidence);
        using var newsDocument = JsonDocument.Parse(news);
        var context = JsonSerializer.Serialize(
            new
            {
                evidence = evidenceDocument.RootElement,
                news = newsDocument.RootElement,
                assessmentContext = ResearchEvidence.Context(
                    run,
                    evidenceDocument.RootElement,
                    newsDocument.RootElement
                ),
            }
        );
        using var document = JsonDocument.Parse(context);
        if (
            document.RootElement.GetProperty("assessmentContext").GetProperty("requiredPositionSymbols").GetRawText()
            != "[\"BTC\"]"
        )
        {
            throw new InvalidOperationException("Reviewer coverage must use wallet holdings, not imported positions.");
        }

        var asOf = document.RootElement.GetProperty("assessmentContext").GetProperty("asOf").GetDateTimeOffset();
        var assessmentTemplate = document.RootElement.GetProperty("assessmentContext");
        if (assessmentTemplate.GetProperty("allowedOpportunitySymbols").GetRawText() != "[\"BTC\",\"ETH\"]")
        {
            throw new InvalidOperationException("Reviewer opportunities must use exact scanned base symbols.");
        }

        var delayedTemplate = JsonSerializer.SerializeToElement(
            ResearchEvidence.Context(
                run with
                {
                    CreatedAt = created.AddMinutes(-16),
                },
                evidenceDocument.RootElement,
                newsDocument.RootElement
            )
        );
        var processingAllowance = TimeSpan.FromSeconds(180 + 45 + 45 + 45);
        if (
            delayedTemplate.GetProperty("earliestReviewAt").GetDateTimeOffset()
            <= delayedTemplate.GetProperty("asOf").GetDateTimeOffset() + processingAllowance
        )
        {
            throw new InvalidOperationException(
                "Review time must allow the worker, final collection and role/audit persistence before it becomes due."
            );
        }

        var valid = new ResearchAssessment(
            "research-assessment-v1",
            account,
            run.Id,
            "policy-1",
            asOf,
            created.AddHours(1),
            ["portfolio:" + run.Id.ToString("D"), "market:" + run.Id.ToString("D"), "news:n1@2"],
            [
                new PositionAssessment(
                    "BTC",
                    "Keep",
                    "No frozen entry thesis",
                    "Review current evidence",
                    created.AddMinutes(5),
                    created.AddHours(1),
                    ["entryThesis"]
                ),
            ],
            [
                new ResearchOpportunity(
                    "ETH",
                    "Setup needs confirmation",
                    "Fresh spread and candle evidence",
                    created.AddMinutes(5)
                ),
            ]
        );
        ResearchAssessmentValidator.Validate(valid, run, account, context);
        using var emptyWalletDocument = JsonDocument.Parse(
            """
            {"policyHash":"policy-1","positions":[{"symbol":"WLD","quantity":1}],"walletEvidence":{"balances":[{"symbol":"WLD","quantity":0},{"symbol":"THB","quantity":5000}]}}
            """
        );
        var emptyContext = JsonSerializer.Serialize(
            new
            {
                evidence = emptyWalletDocument.RootElement,
                news = newsDocument.RootElement,
                assessmentContext = ResearchEvidence.Context(
                    run,
                    emptyWalletDocument.RootElement,
                    newsDocument.RootElement
                ),
            }
        );
        using var emptyContextDocument = JsonDocument.Parse(emptyContext);
        var emptyAssessmentContext = emptyContextDocument.RootElement.GetProperty("assessmentContext");
        if (emptyAssessmentContext.GetProperty("requiredPositionSymbols").GetArrayLength() != 0)
        {
            throw new InvalidOperationException("Historical lots and THB must not become required wallet holdings.");
        }

        if (emptyAssessmentContext.GetProperty("allowedOpportunitySymbols").GetArrayLength() != 0)
        {
            throw new InvalidOperationException("An unavailable scan cannot supply opportunity symbols.");
        }

        using var duplicatedScan = JsonDocument.Parse(
            """
            {"marketResearch":{"scan":{"symbols":["NEAR_THB","NEAR_THB","BTC_USDT","USDT_THB"]}}}
            """
        );
        if (
            JsonSerializer.Serialize(ResearchEvidence.ScannedSymbols(duplicatedScan.RootElement))
            != "[\"NEAR\",\"USDT\"]"
        )
        {
            throw new InvalidOperationException("Opportunity scope must use distinct THB scan bases only.");
        }

        var emptyAssessment = valid with
        {
            AsOf = emptyAssessmentContext.GetProperty("asOf").GetDateTimeOffset(),
            EvidenceIds = ["portfolio:" + run.Id.ToString("D")],
            Positions = [],
            Opportunities = [],
        };
        ResearchAssessmentValidator.Validate(emptyAssessment, run, account, emptyContext);
        CheckRejected(
            emptyAssessment with
            {
                Positions = [valid.Positions[0] with { Symbol = "WLD" }],
            },
            run,
            account,
            emptyContext,
            "historical lots cannot create a current holding",
            WorkerResponseFailure.PositionCoverage
        );
        using var multiHoldingDocument = JsonDocument.Parse(
            """
            {"policyHash":"policy-1","positions":[{"symbol":"ETH","quantity":1}],"walletEvidence":{"balances":[{"symbol":"BTC","quantity":1},{"symbol":"WLD","quantity":2},{"symbol":"THB","quantity":5000}]},"marketResearch":{"available":true,"scan":{"symbols":["BTC_THB","ETH_THB"]}}}
            """
        );
        var multiHoldingContext = JsonSerializer.Serialize(
            new
            {
                evidence = multiHoldingDocument.RootElement,
                news = newsDocument.RootElement,
                assessmentContext = ResearchEvidence.Context(
                    run,
                    multiHoldingDocument.RootElement,
                    newsDocument.RootElement
                ),
            }
        );
        using var multiHoldingContextDocument = JsonDocument.Parse(multiHoldingContext);
        var multiHoldingAssessment = valid with
        {
            AsOf = multiHoldingContextDocument
                .RootElement.GetProperty("assessmentContext")
                .GetProperty("asOf")
                .GetDateTimeOffset(),
            Positions = [valid.Positions[0], valid.Positions[0] with { Symbol = "WLD" }],
        };
        ResearchAssessmentValidator.Validate(multiHoldingAssessment, run, account, multiHoldingContext);
        CheckRejected(
            multiHoldingAssessment with
            {
                Positions = [valid.Positions[0], valid.Positions[0]],
            },
            run,
            account,
            multiHoldingContext,
            "same-count duplicate positions omit a required holding",
            WorkerResponseFailure.PositionCoverage
        );
        using var legacyEvidence = JsonDocument.Parse(
            """
            {"positions":[{"symbol":"ETH","quantity":1},{"symbol":"BTC","quantity":0},{"symbol":"THB","quantity":10},{"symbol":"ETH","quantity":2}]}
            """
        );
        var legacyContext = JsonSerializer.SerializeToElement(
            ResearchEvidence.Context(run, legacyEvidence.RootElement, newsDocument.RootElement)
        );
        if (legacyContext.GetProperty("requiredPositionSymbols").GetRawText() != "[\"ETH\"]")
        {
            throw new InvalidOperationException("Legacy positive holdings must remain distinct and exclude THB.");
        }

        CheckRejected(valid with { AccountKey = new string('b', 64) }, run, account, context, "wrong account");
        CheckRejected(valid with { RunId = Guid.NewGuid() }, run, account, context, "wrong run");
        CheckRejected(valid with { PolicyHash = "wrong" }, run, account, context, "wrong policy");
        CheckRejected(valid with { AsOf = asOf.AddSeconds(-1) }, run, account, context, "altered evidence cutoff");
        CheckRejected(valid with { EvidenceIds = ["news:n1@99"] }, run, account, context, "invented news revision");
        CheckRejected(
            valid with
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
            run,
            account,
            context,
            "expired assessment"
        );
        CheckRejected(
            valid with
            {
                Opportunities = [new ResearchOpportunity("DOGE", "x", "y", created.AddMinutes(5))],
            },
            run,
            account,
            context,
            "opportunity outside scan"
        );
        ResearchAssessmentValidator.Validate(
            valid with
            {
                Positions = [valid.Positions[0] with { MissingFields = [] }],
            },
            run,
            account,
            context
        );
        CheckRejected(
            valid with
            {
                AccountKey = "do-not-leak",
            },
            run,
            account,
            context,
            "lineage diagnostic",
            WorkerResponseFailure.AssessmentLineage
        );
        CheckRejected(
            valid with
            {
                ExpiresAt = created.AddHours(3),
            },
            run,
            account,
            context,
            "lifetime diagnostic",
            WorkerResponseFailure.AssessmentLifetime
        );
        CheckRejected(
            valid with
            {
                EvidenceIds = [],
            },
            run,
            account,
            context,
            "required evidence remains required",
            WorkerResponseFailure.AssessmentBounds
        );
        CheckRejected(
            valid with
            {
                EvidenceIds = Enumerable.Range(0, 17).Select(index => "unknown:" + index).ToArray(),
            },
            run,
            account,
            context,
            "evidence limit remains sixteen",
            WorkerResponseFailure.AssessmentBounds
        );
        CheckRejected(
            valid with
            {
                EvidenceIds = ["do-not-leak"],
            },
            run,
            account,
            context,
            "reference diagnostic",
            WorkerResponseFailure.EvidenceReferences
        );
        CheckRejected(
            valid with
            {
                Positions = [],
            },
            run,
            account,
            context,
            "held position coverage remains required",
            WorkerResponseFailure.PositionCoverage
        );
        CheckRejected(
            valid with
            {
                Positions = [valid.Positions[0], valid.Positions[0]],
            },
            run,
            account,
            context,
            "duplicate positions remain invalid",
            WorkerResponseFailure.PositionCoverage
        );
        CheckRejected(
            valid with
            {
                Positions = [valid.Positions[0] with { MissingFields = [""] }],
            },
            run,
            account,
            context,
            "blank missing field",
            WorkerResponseFailure.PositionContent
        );
        CheckRejected(
            valid with
            {
                Positions = [valid.Positions[0] with { MissingFields = [" \t"] }],
            },
            run,
            account,
            context,
            "whitespace missing field",
            WorkerResponseFailure.PositionContent
        );
        CheckRejected(
            valid with
            {
                Positions = [valid.Positions[0] with { Thesis = " \t" }],
            },
            run,
            account,
            context,
            "blank thesis",
            WorkerResponseFailure.PositionContent
        );
        CheckRejected(
            valid with
            {
                Positions = [valid.Positions[0] with { ReviewAfter = created.AddMinutes(-1) }],
            },
            run,
            account,
            context,
            "expired position review",
            WorkerResponseFailure.PositionContent
        );
        CheckRejected(
            valid with
            {
                Opportunities = [valid.Opportunities[0] with { Reason = " \t" }],
            },
            run,
            account,
            context,
            "blank opportunity",
            WorkerResponseFailure.OpportunityText
        );
        CheckRejected(
            valid with
            {
                Opportunities = [valid.Opportunities[0], valid.Opportunities[0]],
            },
            run,
            account,
            context,
            "duplicate opportunity",
            WorkerResponseFailure.OpportunityDuplicate
        );
        CheckRejected(
            valid with
            {
                Opportunities = [valid.Opportunities[0] with { Symbol = "do-not-leak" }],
            },
            run,
            account,
            context,
            "unscanned opportunity symbol",
            WorkerResponseFailure.OpportunitySymbol
        );
        CheckRejected(
            valid with
            {
                Opportunities = [valid.Opportunities[0] with { ReconsiderWhen = " \t" }],
            },
            run,
            account,
            context,
            "blank reconsideration condition",
            WorkerResponseFailure.OpportunityText
        );
        foreach (var reviewAfter in new[] { created.AddMinutes(-1), valid.ExpiresAt, valid.ExpiresAt.AddMinutes(1) })
        {
            CheckRejected(
                valid with
                {
                    Opportunities = [valid.Opportunities[0] with { ReviewAfter = reviewAfter }],
                },
                run,
                account,
                context,
                "opportunity review must be future and strictly before expiry",
                WorkerResponseFailure.OpportunityReviewTime
            );
        }

        var serverSchedule = valid with
        {
            ExpiresAt = assessmentTemplate.GetProperty("expiresAt").GetDateTimeOffset(),
            Positions =
            [
                valid.Positions[0] with
                {
                    ReviewAfter = assessmentTemplate.GetProperty("earliestReviewAt").GetDateTimeOffset(),
                    ExpiresAt = assessmentTemplate.GetProperty("expiresAt").GetDateTimeOffset(),
                },
            ],
            Opportunities =
            [
                valid.Opportunities[0] with
                {
                    ReviewAfter = assessmentTemplate.GetProperty("earliestReviewAt").GetDateTimeOffset(),
                },
            ],
        };
        ResearchAssessmentValidator.Validate(serverSchedule, run, account, context);
        await RoundTrip();
    }

    private static async Task RoundTrip()
    {
        var account = new string('a', 64);
        var store = new FakeTeamRunStore();
        FakeCodexWorker? worker = null;
        worker = new FakeCodexWorker(
            (role, _, _) =>
            {
                if (role != "reviewer")
                {
                    return Task.FromResult(new ResearchReport(role + " report", null));
                }

                using var context = JsonDocument.Parse(
                    worker?.Calls[^1].Portfolio ?? throw new InvalidOperationException("Missing role context.")
                );
                var template = context.RootElement.GetProperty("assessmentContext");
                if (template.GetProperty("requiredPositionSymbols").GetRawText() != "[\"BTC\"]")
                {
                    throw new InvalidOperationException("Coordinator omitted authoritative reviewer coverage.");
                }

                var asOf = template.GetProperty("asOf").GetDateTimeOffset();
                var expiresAt = template.GetProperty("expiresAt").GetDateTimeOffset();
                var earliestReviewAt = template.GetProperty("earliestReviewAt").GetDateTimeOffset();
                var assessment = new ResearchAssessment(
                    "research-assessment-v1",
                    account,
                    template.GetProperty("runId").GetGuid(),
                    null,
                    asOf,
                    expiresAt,
                    template
                        .GetProperty("evidenceIds")
                        .EnumerateArray()
                        .Select(item => item.GetString() ?? "")
                        .ToArray(),
                    [
                        new PositionAssessment(
                            "BTC",
                            "Review",
                            "Legacy entry thesis unknown",
                            "Inspect supplied evidence",
                            earliestReviewAt,
                            expiresAt,
                            ["entryThesis"]
                        ),
                    ],
                    []
                );
                return Task.FromResult(new ResearchReport("Reviewed supplied news", null, null, null, assessment));
            }
        );
        using var coordinator = new TeamCoordinator(
            store,
            worker,
            new FakePortfolioSnapshotReader { TradingMode = "stopped" },
            account: new FakeCurrentAccountReader(account),
            news: new FakeNewsReader("{\"events\":[{\"eventId\":\"news-one\",\"revision\":2}]}")
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await coordinator.StartAsync(timeout.Token);
        await coordinator.Initialization;
        try
        {
            var run = await coordinator.Start("Read-only news assessment", timeout.Token);
            var completed = await store.WaitFor(
                run.Id,
                item => item.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
                timeout.Token
            );
            if (
                completed.Status != TeamRunStatus.Completed
                || completed.Assessment?.RunId != run.Id
                || completed.Assessment.AccountKey != account
                || !completed.Assessment.EvidenceIds.Contains("news:news-one@2", StringComparer.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "Read-only assessment did not round-trip through the coordinator: " + completed.Error
                );
            }

            var workerPortfolio =
                worker.Calls.Single(call => call.Role == "reviewer").Portfolio
                ?? throw new InvalidOperationException("Missing reviewer portfolio context.");
            using var workerContextDocument = JsonDocument.Parse(workerPortfolio);
            var workerContext = workerContextDocument.RootElement.GetProperty("assessmentContext");
            var persisted =
                completed.Assessment ?? throw new InvalidOperationException("Completed assessment was not persisted.");
            if (
                persisted.ExpiresAt != workerContext.GetProperty("expiresAt").GetDateTimeOffset()
                || persisted.Positions.Single().ReviewAfter
                    != workerContext.GetProperty("earliestReviewAt").GetDateTimeOffset()
                || persisted.Positions.Single().ExpiresAt != workerContext.GetProperty("expiresAt").GetDateTimeOffset()
            )
            {
                throw new InvalidOperationException(
                    "Assessment schedule did not preserve authoritative worker timestamps."
                );
            }
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    private static void CheckRejected(
        ResearchAssessment assessment,
        TeamRun run,
        string account,
        string context,
        string reason,
        WorkerResponseFailure? expectedFailure = null
    )
    {
        try
        {
            ResearchAssessmentValidator.Validate(assessment, run, account, context);
            throw new InvalidOperationException("Accepted invalid assessment: " + reason);
        }
        catch (CodexWorkerException error) when (error.Code == "worker_response_invalid")
        {
            if (expectedFailure is not null && error.ResponseFailure != expectedFailure)
            {
                throw new InvalidOperationException("Wrong safe validation category: " + reason);
            }

            if (error.Message.Contains("do-not-leak", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Validation diagnostics exposed response content.");
            }
        }
    }
}
