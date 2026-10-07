using TradingCompany.Application;
using TradingCompany.Domain;
using System.Security.Cryptography;
using System.Text.Json;
using TradingCompany.AgentRuntime;

namespace TradingCompany.Api;

public sealed class LiveCompanyCoordinator(ILiveCompanyClient client, IHistoricalMarketEvidenceProvider markets,
    IAiCompanyMeetingRunner meeting, IDailyMeetingJournal journal, TimeProvider clock, LiveAccountId accountId, bool continuousReviews = false,
    ICoinResearchProvider? research = null, CompanyLearningService? learning = null)
{
    private readonly SemaphoreSlim tick = new(1, 1);
    private readonly TradingAccountContext context = new(TradingMode.Live, accountId.Value.ToString("N"));
    private readonly HashSet<Guid> published = [];
    public string Status { get; private set; } = "DISABLED";
    public IReadOnlyList<string> ResearchUniverse { get; private set; } = [];
    public IReadOnlyList<string> TradingUniverse { get; private set; } = [];
    public IReadOnlyList<LiveMeetingIssue> Issues { get; private set; } = [];
    public DateTimeOffset? NextRetryAt { get; private set; }
    private DateTimeOffset? retryAfter;

    public void ReportWorkerFailure() => Retry("LIVE_UNAVAILABLE", [new("MEETING_FAILED")]);

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (!await tick.WaitAsync(0, cancellationToken)) return;
        DateOnly? claimedDay = null;
        DailyMeetingSummary? summary = null;
        var unavailableCode = "JOURNAL_UNAVAILABLE";
        var decisionId = Guid.NewGuid();
        try
        {
            var now = clock.GetUtcNow();
            if (journal.AccountContext != context) { Status = "JOURNAL_ACCOUNT_MISMATCH"; return; }
            var day = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
            // Replay saved reports after restart, never replay financial requests.
            foreach (var completed in journal.ReadAll().Where(a => a.Status != "STARTED").TakeLast(31))
                if (!published.Contains(completed.AttemptId))
                { await client.SaveMeetingAsync(completed, cancellationToken); published.Add(completed.AttemptId); }
            journal.EnsureReconciled();
            if (retryAfter > now) return;
            retryAfter = null;
            NextRetryAt = null;
            var saved = journal.Read(day);
            if (!continuousReviews && saved is not null && saved.Status != "STARTED")
            {
                Status = "ALREADY_ATTEMPTED"; return;
            }
            var dailyDue = saved is null && now.ToOffset(TimeSpan.FromHours(7)).Hour >= 9;
            var latest = journal.ReadAll().Where(a => a.Status != "STARTED")
                .OrderBy(a => a.BusinessDate).ThenBy(a => a.ReviewSnapshot?.ObservedAt ??
                    new DateTimeOffset(a.BusinessDate.ToDateTime(new TimeOnly(a.ReviewHour ?? 9, 0)), TimeSpan.FromHours(7))).LastOrDefault();
            Issues = latest?.Summary?.Issues ?? [];
            if (!dailyDue && (!continuousReviews || latest is null) && research is null) { Status = "BEFORE_MEETING"; return; }
            int? reviewHour = continuousReviews && !dailyDue ? now.ToOffset(TimeSpan.FromHours(7)).Hour : null;
            if (!meeting.Enabled || string.IsNullOrWhiteSpace(meeting.Model)) { Status = "MODEL_UNAVAILABLE"; return; }
            unavailableCode = "LIVE_READ_UNAVAILABLE";
            var packet = await client.ReadAsync(accountId, cancellationToken);
            now = clock.GetUtcNow();
            if (!ValidPacket(packet, now)) { Retry("LIVE_BLOCKED", [new("ACCOUNT_STATE_INVALID")]); return; }
            var executionPaused = !packet.Armed || packet.Stopped;
            ResearchUniverse = ResearchSymbols(packet).Where(s => Issues.All(i => i.Symbol != s)).ToArray();
            TradingUniverse = packet.Armed && !packet.Stopped ? packet.Limits.AllowedSymbols.Where(ResearchUniverse.Contains).ToArray() : [];
            var problems = new List<LiveMeetingIssue>();
            CoinResearchBrief? brief = null;
            if (research is not null)
            {
                try
                {
                    brief = await research.ReadAsync(packet, cancellationToken);
                    if (!CoinResearchValidation.IsValid(brief, clock.GetUtcNow())) throw new InvalidDataException();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { brief = null; problems.Add(new("PUBLIC_RESEARCH_UNAVAILABLE")); }
                now = clock.GetUtcNow();
                if (DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime) != day) { Status = "BEFORE_MEETING"; return; }
                // Public document retrieval may be slow. Do not advise against an expired financial snapshot.
                packet = await client.ReadAsync(accountId, cancellationToken);
                now = clock.GetUtcNow();
                if (!ValidPacket(packet, now)) { Retry("LIVE_BLOCKED", [new("ACCOUNT_STATE_INVALID")]); return; }
                executionPaused |= !packet.Armed || packet.Stopped;
                if (DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime) != day) { Status = "BEFORE_MEETING"; return; }
                dailyDue = saved is null && now.ToOffset(TimeSpan.FromHours(7)).Hour >= 9;
                reviewHour = continuousReviews && !dailyDue ? now.ToOffset(TimeSpan.FromHours(7)).Hour : null;
            }
            if (!dailyDue && (!continuousReviews || latest is null)) { Status = "BEFORE_MEETING"; return; }
            unavailableCode = "JOURNAL_UNAVAILABLE";
            if (reviewHour is { } hour && journal.Read(day, hour) is not null)
            { Status = "ALREADY_ATTEMPTED"; NextRetryAt = NextHour(now); return; }
            if (reviewHour is { } currentHour && saved?.ReviewSnapshot is { } dailySnapshot &&
                dailySnapshot.ObservedAt.ToOffset(TimeSpan.FromHours(7)).Hour == currentHour)
            { Status = "ALREADY_ATTEMPTED"; NextRetryAt = NextHour(now); return; }
            journal.EnsureReconciled();
            unavailableCode = "LIVE_READ_UNAVAILABLE";
            var checkpoint = packet.Snapshot with { Quotes = ResearchQuotes(packet) };
            var researchChanged = brief is not null && brief.ChangeKey != latest?.Summary?.Research?.ChangeKey;
            CompanyLearningReport? learningReport = null;
            if (learning is not null)
            {
                try { learningReport = await learning.ReadAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { } // Missing memory never authorizes or blocks an otherwise valid risk decision.
            }
            var learningChanged = learningReport is not null && learningReport.BusinessDate != latest?.Summary?.Learning?.BusinessDate;
            var cutoff = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
            var candidates = new List<AiCompanyCandidateEvidence>();
            foreach (var text in ResearchSymbols(packet))
            {
                try
                {
                    var symbol = new Symbol(text);
                    var evidence = await markets.GetEvidenceAsync(symbol, cutoff.AddDays(-399), cutoff, cancellationToken);
                    var bars = evidence.Bars;
                    if (evidence.Source != "bitkub-daily" || evidence.SourceCurrency != "THB" || bars is null || bars.Count < 200 ||
                        evidence.SourceBars is null || evidence.FxRates is null || evidence.FxRates.Count != 0 ||
                        !bars.SequenceEqual(evidence.SourceBars) || bars[^1].Date != cutoff ||
                        bars.Any(b => b.Symbol != symbol || b.Date > cutoff || b.Date < cutoff.AddDays(-399) ||
                            b.Open <= 0 || b.Close <= 0 || b.Low <= 0 || b.High < b.Low ||
                            b.Open < b.Low || b.Open > b.High || b.Close < b.Low || b.Close > b.High || b.Volume < 0) ||
                        bars.Zip(bars.Skip(1)).Any(pair => pair.First.Date >= pair.Second.Date))
                        throw new InvalidDataException("Completed native market evidence is required.");
                    var forecast = DeterministicForecaster.Evaluate(bars, symbol);
                    var selection = new WalkForwardStrategySelector().Select(bars, symbol,
                        Math.Min(packet.Snapshot.Equity, packet.Limits.CapitalAllocation),
                        new(packet.Limits.FeeReserveRate, packet.Limits.MaximumSlippage));
                    var held = packet.Snapshot.Positions.SingleOrDefault(p => p.Symbol == text);
                    candidates.Add(new($"{text}:{cutoff:yyyy-MM-dd}", symbol, cutoff, bars[^1].Close,
                        held is not null, held?.Quantity ?? 0m, 0m, forecast.Score, forecast.Direction,
                        selection.SelectedStrategy, selection.Promoted, selection.SelectedEvidence?.OutOfSampleReturn ?? 0,
                        selection.SelectedEvidence?.MaxDrawdownPercent ?? 0)
                    {
                        ExecutionAllowed = packet.Limits.AllowedSymbols.Contains(text),
                        CurrentQuote = checkpoint.Quotes.Single(q => q.Symbol == text)
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { problems.Add(new("MARKET_HISTORY_UNAVAILABLE", text)); }
            }
            Issues = problems.ToArray();
            if (candidates.Count == 0) { Retry("RESEARCH_UNAVAILABLE", Issues); return; }
            // Slow evidence retrieval cannot make old balances or quotes current.
            var refreshed = await client.ReadAsync(accountId, cancellationToken);
            now = clock.GetUtcNow();
            if (!ValidPacket(refreshed, now) || refreshed.MandateVersion != packet.MandateVersion ||
                JsonSerializer.Serialize(refreshed.Limits) != JsonSerializer.Serialize(packet.Limits) ||
                !SameOwnedMoney(packet.Snapshot, refreshed.Snapshot))
            { Retry("LIVE_BLOCKED", [new("ACCOUNT_STATE_INVALID")]); return; }
            if (DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime) != day)
            { Status = "BEFORE_MEETING"; return; }
            packet = refreshed;
            executionPaused |= !packet.Armed || packet.Stopped;
            checkpoint = packet.Snapshot with { Quotes = ResearchQuotes(packet) };
            candidates = candidates.Select(c => c with { CurrentQuote = checkpoint.Quotes.Single(q => q.Symbol == c.Symbol.Value) }).ToList();
            var missingHeldEvidence = problems.Any(i => i.Code == "MARKET_HISTORY_UNAVAILABLE" &&
                packet.Snapshot.Positions.Any(p => p.Symbol == i.Symbol));
            var advisoryOnly = executionPaused || missingHeldEvidence || problems.Any(i => i.Code == "PUBLIC_RESEARCH_UNAVAILABLE") || continuousReviews &&
                (packet.Snapshot.CashReserved > 0 || packet.Snapshot.PendingBuyNotional > 0 ||
                 packet.Snapshot.PendingSellNotional > 0 || packet.Snapshot.Positions.Any(p => p.ReservedQuantity > 0));
            ResearchUniverse = candidates.Select(c => c.Symbol.Value).ToArray();
            TradingUniverse = advisoryOnly ? [] : candidates.Where(c => c.ExecutionAllowed).Select(c => c.Symbol.Value).ToArray();
            var problemsChanged = !(latest?.Summary?.Issues ?? []).SequenceEqual(Issues) ||
                (latest?.Summary?.AdvisoryOnly ?? false) != advisoryOnly;
            if (reviewHour is not null && !researchChanged && !learningChanged && !problemsChanged && latest?.Summary?.Reports.Count > 0 &&
                latest.ReviewSnapshot is { } previous && !NeedsReview(previous, checkpoint))
            { Status = advisoryOnly ? "READ_ONLY" : "WATCHING"; NextRetryAt = clock.GetUtcNow().AddSeconds(30); return; }
            unavailableCode = "JOURNAL_UNAVAILABLE";
            var begun = continuousReviews ? journal.TryBegin(day, decisionId, reviewHour, checkpoint) : journal.TryBegin(day, decisionId);
            if (!begun) { Status = "ALREADY_ATTEMPTED"; NextRetryAt = NextHour(clock.GetUtcNow()); return; }
            claimedDay = day;
            unavailableCode = "MEETING_FAILED";
            summary = new(0, 0, 0, []) { Issues = problems.Count == 0 ? null : Issues, AdvisoryOnly = advisoryOnly, Research = brief };

            var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { packet, candidates, brief })));
            var snapshot = packet.Snapshot;
            var input = new AiCompanyMeetingInput(cutoff, hash,
                new(Math.Min(snapshot.Equity, packet.Limits.CapitalAllocation), snapshot.CashBalance - snapshot.CashReserved,
                    snapshot.Positions.Sum(p => p.Quantity * p.MarketPrice) + snapshot.PendingBuyNotional,
                    snapshot.DailyLoss, snapshot.Drawdown, snapshot.Positions.Count), candidates,
                "Live account advisory meeting. Use supplied actual balances, completed bars and separately timestamped public research only. No leverage or borrowing. " +
                "Unrealized P&L is unavailable; its zero field is a placeholder. All proposals require independent executor risk validation. " +
                (advisoryOnly ? "This meeting is advisory only. Execution is paused; no proposal from this round will be submitted. " : "") +
                (problems.Count == 0 ? "" : "Evidence gaps: " + JsonSerializer.Serialize(problems) + ". Discuss these gaps; do not invent missing data. "),
                packet.Limits.MaximumPositionWeight, packet.Limits.MaximumOpenPositions)
            {
                AccountContext = context, PortfolioReview = reviewHour is not null && latest?.Summary?.Reports.Count > 0,
                ObservedAt = now, Research = brief, ResearchChanged = researchChanged,
                Learning = learningReport, LearningChanged = learningChanged,
                PreviousConclusion = reviewHour is null ? null : latest?.Summary?.Reports
                    .SingleOrDefault(r => r.Role == "portfolio-final")?.Summary
            };
            var result = await meeting.RunAsync(input, cancellationToken);
            summary = Summarize(result) with { Research = brief, Issues = problems.Count == 0 ? null : Issues,
                AdvisoryOnly = advisoryOnly, Learning = learningReport };
            if (result.Status != "COMPLETED") { Finish(result.Status); return; }
            if (result.AccountContext != context || !ValidProposals(result.Proposals, packet.Limits) ||
                result.Proposals.Any(p => candidates.All(c => c.Symbol != p.Symbol))) { Finish("INVALID_FINAL"); return; }
            // A round started without execution authority can never gain it during the meeting.
            if (advisoryOnly) { Finish("READ_ONLY"); return; }
            foreach (var proposal in result.Proposals.Where(p => p.Action != PortfolioAction.Hold)
                .OrderBy(p => p.Action == PortfolioAction.Buy ? 1 : 0))
            {
                // Polling may refresh the version and quote while advice is produced. Only
                // unchanged owned money/quantities and bounded prices can use that advice.
                var current = await client.ReadAsync(accountId, cancellationToken);
                if (DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime) != day ||
                    !ValidPacket(current, clock.GetUtcNow()) || !current.Armed || current.Stopped || current.MandateVersion != packet.MandateVersion ||
                    JsonSerializer.Serialize(current.Limits) != JsonSerializer.Serialize(packet.Limits) ||
                    !SameOwnedMoney(snapshot, current.Snapshot)) { Finish("SNAPSHOT_CHANGED"); return; }
                var quote = current.Snapshot.Quotes.Single(q => q.Symbol == proposal.Symbol.Value);
                var originalQuote = snapshot.Quotes.Single(q => q.Symbol == proposal.Symbol.Value);
                if (Math.Abs(quote.Ask - originalQuote.Ask) > originalQuote.Ask * packet.Limits.MaximumSlippage ||
                    Math.Abs(quote.Bid - originalQuote.Bid) > originalQuote.Bid * packet.Limits.MaximumSlippage)
                { Finish("PRICE_CHANGED"); return; }
                var sizing = current.Limits.OrderSizing.Single(s => s.Symbol == proposal.Symbol.Value);
                var held = current.Snapshot.Positions.SingleOrDefault(p => p.Symbol == proposal.Symbol.Value);
                var equity = Math.Min(current.Snapshot.Equity, packet.Limits.CapitalAllocation);
                var side = proposal.Action == PortfolioAction.Buy ? OrderSide.Buy : OrderSide.Sell;
                var price = decimal.Floor((side == OrderSide.Buy ? quote.Ask : quote.Bid) / sizing.PriceStep) * sizing.PriceStep;
                if (price <= 0) continue;
                var quantity = side == OrderSide.Buy ? Math.Min(Math.Max(0, proposal.TargetWeight * equity - (held?.Quantity ?? 0) * price),
                    (current.Snapshot.CashBalance - current.Snapshot.CashReserved) / (1 + current.Limits.FeeReserveRate)) / price
                    : proposal.Action == PortfolioAction.Sell ? held?.Quantity ?? 0
                    : (held?.Quantity ?? 0) - proposal.TargetWeight * equity / price;
                var fees = 1 + current.Limits.FeeReserveRate;
                var turnoverLeft = (current.Limits.MaximumDailyTurnover ?? current.Limits.CapitalAllocation) -
                    current.Snapshot.DailyTurnover - (current.Snapshot.PendingBuyNotional + current.Snapshot.PendingSellNotional) * fees;
                var orderCeiling = Math.Max(0, Math.Min(current.Limits.MaximumOrderNotional ?? current.Limits.CapitalAllocation,
                    turnoverLeft)) / fees;
                quantity = Math.Min(quantity, orderCeiling / price);
                if (side == OrderSide.Sell) quantity = Math.Min(quantity, (held?.Quantity ?? 0) - (held?.ReservedQuantity ?? 0));
                quantity = decimal.Floor(quantity / sizing.BaseStep) * sizing.BaseStep;
                if (quantity <= 0 || (side == OrderSide.Sell && (held is null || quantity > held.Quantity - held.ReservedQuantity))) continue;
                var budget = quantity * price;
                if ((side == OrderSide.Buy ? decimal.Floor(budget / (1 + current.Limits.FeeReserveRate) / sizing.QuoteStep) * sizing.QuoteStep
                    : budget) < sizing.MinimumQuoteSize) continue;
                var executionHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { advice = hash, current, proposal })));
                var intent = new LiveIntent(accountId, decisionId, Guid.NewGuid(), TradingMode.Live, proposal.Symbol.Value,
                    side, quantity, price, packet.MandateVersion, current.Snapshot.Version, executionHash)
                    { BuyBudget = side == OrderSide.Buy ? budget : null };
                var order = await client.SubmitAsync(intent, cancellationToken);
                if (order.Intent != intent) { Finish("EXECUTOR_REPLY_INVALID"); return; }
                // Execution changes the snapshot version. Refresh/research is required for
                // further proposals. One non-HOLD intent per saved meeting/review.
                Finish(order.State == LiveOrderState.Rejected ? "RISK_REJECTED" : "ORDER_PENDING");
                return;
            }
            Finish("HOLD");
        }
        catch (OperationCanceledException)
        {
            // Record this attempt without replaying it. Any submitted order and
            // unresolved financial outcome remain the executor's responsibility.
            Finish("INTERRUPTED");
            throw;
        }
        catch (Exception)
        {
            if (claimedDay is not null) Finish("LIVE_UNAVAILABLE");
            else Retry("LIVE_UNAVAILABLE", [new(unavailableCode)]);
        }
        finally { tick.Release(); }

        void Finish(string status)
        {
            Status = status;
            NextRetryAt = NextHour(clock.GetUtcNow());
            if (status is not ("READ_ONLY" or "HOLD" or "ORDER_PENDING" or "RISK_REJECTED"))
            {
                Issues = [.. Issues, new("MEETING_FAILED")];
                if (summary is not null) summary = summary with { Issues = Issues };
            }
            if (claimedDay is { } day) journal.Complete(day, decisionId, status, summary);
            claimedDay = null;
        }
    }

    private static DailyMeetingSummary Summarize(AiCompanyMeetingResult result)
    {
        var reports = new List<SavedAiReport>();
        foreach (var artifact in result.Artifacts)
        {
            using var output = JsonDocument.Parse(artifact.OutputJson);
            if (!output.RootElement.TryGetProperty("summary", out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString())) continue;
            var text = value.GetString()!;
            reports.Add(new(artifact.Key.Role, text.Length <= 6000 ? text : text[..6000], artifact.ProviderModel,
                artifact.InputTokens, artifact.OutputTokens, artifact.ArtifactId.ToString(), artifact.Key.EvidenceHash, artifact.Key.DataCutoff));
        }
        return new(result.ModelCalls, result.InputTokens, result.OutputTokens, reports);
    }

    private bool ValidPacket(LiveResearchPacket? packet, DateTimeOffset now)
    {
        if (packet is null || packet.AccountContext != context || !TradingAccountContext.IsValid(context) ||
            packet.MandateVersion <= 0 || packet.Snapshot is not { } s || packet.Limits is not { } m ||
            s.AccountId != accountId || s.Version <= 0 || !s.Reconciled || s.ObservedAt > now || now - s.ObservedAt > TimeSpan.FromSeconds(30) ||
            s.Equity <= 0 || s.CashBalance < 0 || s.CashReserved < 0 || s.CashReserved > s.CashBalance ||
            m.CapitalAllocation <= 0 || m.MaximumPositionWeight is <= 0 or > 1m || m.MaximumOpenPositions <= 0 ||
            m.MaximumOrderNotional is <= 0 || m.MaximumDailyTurnover is <= 0 ||
            m.FeeReserveRate is <= 0 or > .1m || m.MaximumSlippage is < 0 or > .02m ||
            m.AllowedSymbols is not { Count: > 0 } || m.AllowedSymbols.Any(symbol => symbol is not ("BTC-THB" or "ETH-THB" or "SOL-THB")) ||
            m.AllowedSymbols.Distinct().Count() != m.AllowedSymbols.Count || s.Positions is null || s.Quotes is null ||
            s.PendingBuySymbols is null || s.PendingBuyNotional < 0 || s.PendingSellNotional < 0 ||
            s.DailyLoss < 0 || s.Drawdown is < 0 or > 1 || s.DailyTurnover < 0 ||
            s.PendingBuySymbols.Any(symbol => !m.AllowedSymbols.Contains(symbol)) ||
            s.Positions.Any(p => p is null || p.Quantity <= 0 || p.ReservedQuantity < 0 || p.ReservedQuantity > p.Quantity ||
                p.MarketPrice <= 0 || !m.AllowedSymbols.Contains(p.Symbol)) ||
            s.Positions.Select(p => p.Symbol).Distinct().Count() != s.Positions.Count ||
            s.Quotes.Any(q => q is null) || s.Quotes.Select(q => q.Symbol).Distinct().Count() != s.Quotes.Count) return false;
        try { if (s.Equity != s.CashBalance + s.Positions.Sum(p => p.Quantity * p.MarketPrice)) return false; }
        catch (OverflowException) { return false; }
        if (m.OrderSizing is null || m.OrderSizing.Any(p => p is null || p.PriceStep <= 0 || p.BaseStep <= 0 || p.QuoteStep <= 0 || p.MinimumQuoteSize <= 0) ||
            m.OrderSizing.Select(p => p.Symbol).Distinct().Count() != m.OrderSizing.Count ||
            m.AllowedSymbols.Any(symbol => m.OrderSizing.Count(p => p.Symbol == symbol) != 1)) return false;
        if (!m.AllowedSymbols.All(symbol => s.Quotes.Count(q => q.Symbol == symbol && q.Bid > 0 && q.Ask >= q.Bid &&
            q.ObservedAt <= now && now - q.ObservedAt <= TimeSpan.FromSeconds(30)) == 1) ||
            m.ResearchSymbols is null || packet.ResearchQuotes is null) return false;
        var research = ResearchSymbols(packet);
        var quotes = ResearchQuotes(packet);
        return research.Count is > 0 and <= 3 && research.Distinct().Count() == research.Count &&
            research.All(symbol => symbol is "BTC-THB" or "ETH-THB" or "SOL-THB") &&
            m.AllowedSymbols.All(research.Contains) && quotes.Count == research.Count &&
            quotes.All(q => q is not null && research.Contains(q.Symbol) && q.Bid > 0 && q.Ask >= q.Bid &&
                q.ObservedAt <= now && now - q.ObservedAt <= TimeSpan.FromSeconds(30)) &&
            quotes.Select(q => q.Symbol).Distinct().Count() == quotes.Count;
    }

    private void Retry(string status, IReadOnlyList<LiveMeetingIssue> problems)
    {
        Status = status; Issues = problems.ToArray(); TradingUniverse = [];
        retryAfter = clock.GetUtcNow().AddMinutes(5); NextRetryAt = retryAfter;
    }

    private static DateTimeOffset NextHour(DateTimeOffset now)
    {
        var local = now.ToOffset(TimeSpan.FromHours(7));
        return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset).AddHours(1);
    }

    private static IReadOnlyList<string> ResearchSymbols(LiveResearchPacket packet) =>
        packet.Limits.ResearchSymbols.Count == 0 ? packet.Limits.AllowedSymbols : packet.Limits.ResearchSymbols;

    private static IReadOnlyList<LiveQuote> ResearchQuotes(LiveResearchPacket packet) =>
        packet.ResearchQuotes.Count == 0 ? packet.Snapshot.Quotes : packet.ResearchQuotes;

    private static bool NeedsReview(LiveSnapshot before, LiveSnapshot current) => !SameOwnedMoney(before, current) ||
        before.Quotes.Count != current.Quotes.Count || current.Quotes.Any(q =>
            before.Quotes.SingleOrDefault(p => p.Symbol == q.Symbol) is not { } old || old.Bid <= 0 || old.Ask <= 0 ||
            Math.Abs(q.Bid - old.Bid) >= old.Bid * .01m || Math.Abs(q.Ask - old.Ask) >= old.Ask * .01m);

    private static bool SameOwnedMoney(LiveSnapshot before, LiveSnapshot after) => after.Version >= before.Version &&
        before.CashBalance == after.CashBalance && before.CashReserved == after.CashReserved &&
        before.PendingBuyNotional == after.PendingBuyNotional && before.PendingSellNotional == after.PendingSellNotional &&
        before.DailyTurnover == after.DailyTurnover &&
        before.PendingBuySymbols.Order(StringComparer.Ordinal).SequenceEqual(after.PendingBuySymbols.Order(StringComparer.Ordinal)) &&
        before.Positions.OrderBy(p => p.Symbol, StringComparer.Ordinal).Select(p => (p.Symbol, p.Quantity, p.ReservedQuantity))
            .SequenceEqual(after.Positions.OrderBy(p => p.Symbol, StringComparer.Ordinal).Select(p => (p.Symbol, p.Quantity, p.ReservedQuantity)));

    private static bool ValidProposals(IReadOnlyList<AiCompanyProposal>? proposals, LiveResearchLimits limits) => proposals is not null &&
        proposals.Select(p => p.Symbol).Distinct().Count() == proposals.Count && proposals.All(p => Enum.IsDefined(p.Action) &&
            limits.AllowedSymbols.Contains(p.Symbol.Value) && p.TargetWeight >= 0 && p.TargetWeight <= limits.MaximumPositionWeight &&
            p.Confidence is >= 0 and <= 1 && !string.IsNullOrWhiteSpace(p.Thesis) && !string.IsNullOrWhiteSpace(p.Invalidation));
}
