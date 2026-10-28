using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Company;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class CompanyPaperCycle
{
    public static async Task<CompanySimulationCycle> Run(
        CompanySimulationRequest request,
        CompanyCapitalReport? capital,
        CompanyCapitalEvidenceState evidence,
        TradingRuleSet rules,
        CompanySimulationCycle? previous,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        ValidateRequest(request, rules);
        cancellationToken.ThrowIfCancellationRequested();
        var hash = TradingRuleHash.Compute(rules);
        var reasons = CompanyCapitalReadinessGate
            .Inspect(
                capital,
                evidence.Accounts.Select(account => account.AccountKey).ToArray(),
                now,
                TimeSpan.FromSeconds(rules.MaxDataAgeSeconds)
            )
            .Reasons.ToList();
        reasons.AddRange(CapitalIssues(request, capital, evidence));
        reasons.AddRange(MarketIssues(request, rules, now));
        if (previous is not null)
        {
            if (previous.Status != "completed" || previous.State is null || previous.Capital is null)
            {
                throw new InvalidDataException("Only a completed Paper cycle can supply the next ledger.");
            }

            CompanyPaperStore.Validate(previous.State, hash);
            if (
                request.CapitalFrom != previous.Capital.To
                || capital?.OpeningEquityThb != previous.Capital.ClosingEquityThb
                || request.Frames[0].At <= previous.State.HeartbeatAt
            )
            {
                reasons.Add(
                    "Capital intervals and simulation frames must continue the retained cycle without overlap or gaps."
                );
            }
        }

        var contribution = previous is null ? 0 : capital?.NetContributionsThb;
        var transferFees = previous is null ? 0 : capital?.InternalTransferFeesThb;
        var state = previous?.State;
        var opening = previous?.ClosingPaperEquityThb ?? capital?.ClosingEquityThb;
        if (previous is null && capital?.ClosingEquityThb < rules.AllocatedThb)
        {
            reasons.Add("Verified fixture cash is below the unchanged allocation cap.");
        }

        if (
            state is not null
            && state.Paper.CashThb + contribution - transferFees < TradingLedgerAccounting.ReservedCash(state.Paper)
        )
        {
            reasons.Add("A contribution withdrawal or transfer fee exceeds unreserved simulated cash.");
        }

        if (reasons.Count != 0 || opening is null || contribution is null || transferFees is null)
        {
            return Blocked(request, capital, hash, reasons.Distinct(StringComparer.Ordinal).ToArray(), state);
        }

        state ??= Initial(
            rules,
            capital?.ClosingEquityThb ?? throw new InvalidDataException("Missing admitted capital."),
            request.CapitalTo
        );
        state = state with
        {
            Paper = state.Paper with
            {
                CashThb = state.Paper.CashThb + contribution.Value - transferFees.Value,
                FundingThb = state.Paper.FundingThb + contribution.Value,
            },
        };
        var firstFill = state.Paper.Fills.Count;
        var firstDecision = state.Decisions.Count;
        var store = new CompanyPaperStore(state);
        var writer = new TradingStateWriter(store);
        var processor = new TradingSignalProcessor(store, store, null);
        var fillIndex = 0;
        var execution = new TradingPaperExecution(
            store,
            () =>
                new Guid(
                    SHA256
                        .HashData(
                            Encoding.UTF8.GetBytes(
                                hash + ":" + request.Id + ":" + (fillIndex++).ToString(CultureInfo.InvariantCulture)
                            )
                        )
                        .AsSpan(0, 16)
                )
        );
        foreach (var frame in request.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state = await execution.Fill(state, frame.Books, frame.At, cancellationToken);
            state = await writer.Save(
                state,
                state with
                {
                    HeartbeatAt = frame.At,
                    Paper = TradingLedgerValuation.Mark(state.Paper, frame.Books, rules, frame.At, true),
                },
                cancellationToken
            );
            (state, _) = await processor.Process(state, frame.Books, null, null, false, frame.At, cancellationToken);
            state = await execution.Fill(state, frame.Books, frame.At, cancellationToken);
            state = await writer.Save(
                state,
                state with
                {
                    Paper = TradingLedgerValuation.Mark(state.Paper, frame.Books, rules, frame.At, true),
                },
                cancellationToken
            );
        }

        CompanyPaperStore.Validate(state, hash);
        var closing =
            state.Paper.Equity[^1].EquityThb ?? throw new InvalidDataException("Paper closing valuation is unknown.");
        var fills = state.Paper.Fills.Skip(firstFill).ToArray();
        var decisions = state.Decisions.Skip(firstDecision).ToArray();
        var net = fills
            .Where(fill => fill.Side == TradeSide.Sell)
            .Sum(fill => fill.RealizedNetThb ?? throw new InvalidDataException("Paper realized fill is unknown."));
        var fees = fills.Sum(fill => fill.FeeThb);
        return new(
            request,
            capital,
            hash,
            "completed",
            [],
            [
                new(
                    "finance",
                    "completed",
                    "Admitted capital revision "
                        + evidence.Revision.ToString(CultureInfo.InvariantCulture)
                        + "; external contributions and internal fees remain separate from Paper outcomes."
                ),
                new(
                    "management",
                    "completed",
                    "Objective "
                        + request.ObjectiveVersion
                        + "; fixed allocation, per-order and daily-loss limits retained. A missed target never grants authority or raises risk."
                ),
                new(
                    "research",
                    "completed",
                    string.Join(
                        " | ",
                        decisions
                            .Select(decision => decision.Symbol + ": " + decision.Reason)
                            .Distinct(StringComparer.Ordinal)
                    )
                ),
                new(
                    "risk",
                    "completed",
                    "Existing signal processor enforced cash, allocation, position, precision, cooldown and daily-loss guards; rules "
                        + hash
                        + "."
                ),
                new(
                    "paperExecution",
                    "completed",
                    fills.Length.ToString(CultureInfo.InvariantCulture)
                        + " simulated fills; existing observed-liquidity and fee calculations; no exchange port."
                ),
                new(
                    "reviewer",
                    "completed",
                    "Paper outcomes include costs and losses; deposits are not profit. No operating-cost evidence or verified company income; no live eligibility."
                ),
            ],
            state,
            opening,
            closing,
            contribution,
            transferFees,
            net,
            fees,
            closing - opening.Value - contribution.Value,
            Math.Max(0, request.TargetThb - closing)
        );
    }

    public static void ValidateRequest(CompanySimulationRequest request, TradingRuleSet rules)
    {
        rules.Validate();
        if (
            rules.Version != "ema20-50-breakout-v1"
            || !Reference(request.Id)
            || !Reference(request.ObjectiveVersion)
            || request.TargetThb is <= 0 or > 1000000000000m
            || request.Deadline <= request.CapitalTo
            || request.CapitalFrom >= request.CapitalTo
            || request.Frames.Count is < 1 or > 120
            || request.Frames.Any(frame => frame is null || frame.Books is null || frame.Books.Count > 16)
        )
        {
            throw new InvalidDataException(
                "Unsupported simulation request, objective, fixed strategy or frame budget."
            );
        }

        DateTimeOffset? previous = null;
        foreach (var frame in request.Frames)
        {
            if (
                frame.At < request.CapitalTo
                || frame.At <= previous
                || frame.Books.Any(book => book is null || book.Bars is null || book.Bars.Count > 300)
            )
            {
                throw new InvalidDataException(
                    "Simulation frames must be ordered and bounded after the capital cutoff."
                );
            }

            previous = frame.At;
        }
    }

    private static IReadOnlyList<string> CapitalIssues(
        CompanySimulationRequest request,
        CompanyCapitalReport? capital,
        CompanyCapitalEvidenceState evidence
    )
    {
        var receipts = evidence
            .Receipts.Where(receipt =>
                receipt.Import.Period.Previous.ObservedAt == request.CapitalFrom
                && receipt.Import.Period.Current.ObservedAt == request.CapitalTo
            )
            .ToArray();
        if (
            capital is null
            || capital.From != request.CapitalFrom
            || capital.To != request.CapitalTo
            || capital.ImportRevision != evidence.Revision
            || !capital
                .ImportDigests.ToHashSet(StringComparer.Ordinal)
                .SetEquals(receipts.Select(receipt => receipt.Digest))
            || evidence.Accounts.Any(account =>
                account is not { Broker: "bitkub", LegalRegion: "TH", Environment: "offlineFixture" }
            )
        )
        {
            return ["Capital snapshot, provenance and exact fixture journal revision do not agree."];
        }

        if (
            receipts
                .SelectMany(receipt => new[] { receipt.Import.Period.Previous, receipt.Import.Period.Current })
                .Any(snapshot =>
                    snapshot.SpendableThb != snapshot.EquityThb
                    || snapshot.ExposureThb != 0
                    || snapshot.ReservedThb != 0
                    || snapshot.Assets.Any(asset =>
                        asset.Quantity > 0
                        && (asset.Symbol != "THB" || asset.PriceThb != 1 || asset.Available != asset.Quantity)
                    )
                )
            || capital.NetEquityChangeThb != -capital.InternalTransferFeesThb
            || capital.InternalTransferNetThb != 0
        )
        {
            return
            [
                "This simulation admits unreserved native THB cash only; asset liquidation, FX and broker trading returns are not assumed.",
            ];
        }

        return [];
    }

    private static IReadOnlyList<string> MarketIssues(
        CompanySimulationRequest request,
        TradingRuleSet rules,
        DateTimeOffset now
    )
    {
        foreach (var frame in request.Frames)
        {
            if (
                frame.At > now
                || frame.At - request.CapitalTo > TimeSpan.FromSeconds(rules.MaxDataAgeSeconds)
                || !rules.Symbols.ToHashSet(StringComparer.Ordinal).SetEquals(frame.Books.Select(book => book.Symbol))
                || frame.Books.Select(book => book.Symbol).Distinct(StringComparer.Ordinal).Count() != frame.Books.Count
            )
            {
                return ["Frame chronology, capital freshness or configured market coverage is incomplete."];
            }

            foreach (var book in frame.Books)
            {
                if (
                    !TradingExecutionRules.Fresh(book, rules, frame.At)
                    || book.BidQuantity < 0
                    || book.AskQuantity < 0
                    || book.MinimumNotional <= 0
                    || book.TimeframeSeconds != 300
                    || book.Bars.Count < 51
                )
                {
                    return ["Market quote, precision, liquidity or completed-candle evidence is missing or stale."];
                }

                long? previous = null;
                foreach (var bar in book.Bars)
                {
                    if (
                        bar.Timestamp % 300 != 0
                        || bar.Timestamp < 0
                        || previous is { } at && bar.Timestamp - at != 300
                        || bar.Timestamp > book.ObservedAt.ToUnixTimeSeconds() - 300
                        || bar.Low <= 0
                        || bar.Volume < 0
                        || bar.High < Math.Max(bar.Open, bar.Close)
                        || bar.Low > Math.Min(bar.Open, bar.Close)
                    )
                    {
                        return ["Market candles contain gaps, malformed values or future information."];
                    }

                    previous = bar.Timestamp;
                }

                if (frame.At.ToUnixTimeSeconds() - book.Bars[^1].Timestamp > 600)
                {
                    return ["The latest completed candle is stale."];
                }
            }
        }

        return [];
    }

    private static TradingState Initial(TradingRuleSet rules, decimal cash, DateTimeOffset at)
    {
        var state = TradingStateFactory.Create(at);
        return state with
        {
            Rules = rules,
            RulesHash = TradingRuleHash.Compute(rules),
            Paper = new(cash, cash, at, [], [], [], [new(at, cash, cash)]),
        };
    }

    private static CompanySimulationCycle Blocked(
        CompanySimulationRequest request,
        CompanyCapitalReport? capital,
        string hash,
        IReadOnlyList<string> reasons,
        TradingState? state
    ) =>
        new(
            request,
            capital,
            hash,
            "blocked",
            reasons,
            [
                new("finance", "blocked", string.Join(" ", reasons)),
                new("management", "deferred", "No plan allocation without admitted evidence."),
                new("research", "deferred", "No strategy conclusion from incomplete evidence."),
                new("risk", "blocked", "Existing limits retained; no risk escalation."),
                new("paperExecution", "skipped", "No simulated or real fills."),
                new("reviewer", "completed", "Blocked evidence and Unknown income retained."),
            ],
            state,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );

    private static bool Reference(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
}
