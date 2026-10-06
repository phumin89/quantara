using TradingCompany.Application;
using TradingCompany.Domain;
using System.Security.Cryptography;
using System.Text.Json;

namespace TradingCompany.Api;

public sealed class LiveCompanyCoordinator(ILiveCompanyClient client, IHistoricalMarketEvidenceProvider markets,
    IAiCompanyMeetingRunner meeting, IDailyMeetingJournal journal, TimeProvider clock, LiveAccountId accountId)
{
    private readonly SemaphoreSlim tick = new(1, 1);
    private readonly TradingAccountContext context = new(TradingMode.Live, accountId.Value.ToString("N"));
    private readonly HashSet<Guid> published = [];
    public string Status { get; private set; } = "DISABLED";

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (!await tick.WaitAsync(0, cancellationToken)) return;
        DateOnly? claimedDay = null;
        DailyMeetingSummary? summary = null;
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
            var saved = journal.Read(day);
            if (saved is not null && saved.Status != "STARTED")
            {
                Status = "ALREADY_ATTEMPTED"; return;
            }
            if (now.ToOffset(TimeSpan.FromHours(7)).TimeOfDay < TimeSpan.FromHours(9)) { Status = "BEFORE_MEETING"; return; }
            if (!meeting.Enabled || string.IsNullOrWhiteSpace(meeting.Model)) { Status = "MODEL_UNAVAILABLE"; return; }
            var packet = await client.ReadAsync(accountId, cancellationToken);
            if (!ValidPacket(packet, now)) { Status = "LIVE_BLOCKED"; return; }
            journal.EnsureReconciled();
            if (!journal.TryBegin(day, decisionId)) { Status = "ALREADY_ATTEMPTED"; return; }
            claimedDay = day;
            var cutoff = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
            var candidates = new List<AiCompanyCandidateEvidence>();
            try
            {
                foreach (var text in packet.Limits.AllowedSymbols)
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
                        selection.SelectedEvidence?.MaxDrawdownPercent ?? 0));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { Finish("RESEARCH_UNAVAILABLE"); return; }

            var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { packet, candidates })));
            var snapshot = packet.Snapshot;
            var input = new AiCompanyMeetingInput(cutoff, hash,
                new(Math.Min(snapshot.Equity, packet.Limits.CapitalAllocation), snapshot.CashBalance - snapshot.CashReserved,
                    snapshot.Positions.Sum(p => p.Quantity * p.MarketPrice) + snapshot.PendingBuyNotional,
                    snapshot.DailyLoss, snapshot.Drawdown, snapshot.Positions.Count), candidates,
                "Live account advisory meeting. Use supplied actual balances and completed bars only. No leverage or borrowing. " +
                "Unrealized P&L is unavailable; its zero field is a placeholder. All proposals require independent executor risk validation.",
                packet.Limits.MaximumPositionWeight, packet.Limits.MaximumOpenPositions) { AccountContext = context };
            var result = await meeting.RunAsync(input, cancellationToken);
            summary = Summarize(result);
            if (result.Status != "COMPLETED") { Finish(result.Status); return; }
            if (result.AccountContext != context || !ValidProposals(result.Proposals, packet.Limits)) { Finish("INVALID_FINAL"); return; }
            foreach (var proposal in result.Proposals.Where(p => p.Action != PortfolioAction.Hold))
            {
                // Polling may refresh the version and quote while advice is produced. Only
                // unchanged owned money/quantities and bounded prices can use that advice.
                var current = await client.ReadAsync(accountId, cancellationToken);
                if (DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime) != day ||
                    !ValidPacket(current, clock.GetUtcNow()) || current.MandateVersion != packet.MandateVersion ||
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
                // further proposals; one non-HOLD intent per daily meeting in this first slice.
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
        catch (Exception) { if (claimedDay is not null) Finish("LIVE_UNAVAILABLE"); else Status = "LIVE_UNAVAILABLE"; }
        finally { tick.Release(); }

        void Finish(string status)
        {
            Status = status;
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
            !packet.Armed || packet.Stopped || packet.MandateVersion <= 0 || packet.Snapshot is not { } s || packet.Limits is not { } m ||
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
        return m.AllowedSymbols.All(symbol => s.Quotes.Count(q => q.Symbol == symbol && q.Bid > 0 && q.Ask >= q.Bid &&
            q.ObservedAt <= now && now - q.ObservedAt <= TimeSpan.FromSeconds(30)) == 1);
    }

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
