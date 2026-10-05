using TradingCompany.Application;
using TradingCompany.Infrastructure;
using TradingCompany.Domain;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingCompany.Api;

public sealed record ForwardPaperCompanyStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("modelEnabled")] bool ModelEnabled,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("lastMeetingDate")] DateOnly? LastMeetingDate,
    [property: JsonPropertyName("supportedSymbols")] IReadOnlyList<string> SupportedSymbols,
    [property: JsonPropertyName("quoteCredentialsConfigured")] bool? QuoteCredentialsConfigured,
    [property: JsonPropertyName("publicCryptoQuotes")] bool PublicCryptoQuotes,
    [property: JsonPropertyName("equityQuotesConfigured")] bool? EquityQuotesConfigured,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("schedule")] string Schedule,
    [property: JsonPropertyName("executionCosts")] string ExecutionCosts,
    [property: JsonPropertyName("lastMeeting")] DailyMeetingAttempt? LastMeeting,
    [property: JsonPropertyName("paused")] bool Paused = false,
    [property: JsonPropertyName("busy")] bool Busy = false);

public sealed class ForwardPaperCompanyService(IForwardFundingStore funding, IForwardInvestmentStore legacy,
    JsonForwardPaperStore paper, IAiCompanyMeetingRunner meeting, IHistoricalMarketDataProvider history,
    IForwardPaperQuotes quotes, IDailyMeetingJournal journal, TimeProvider clock, IConfiguration configuration,
    ILogger<ForwardPaperCompanyService> logger, SandboxControl? sandboxControl = null) : BackgroundService
{
    private readonly SandboxControl control = sandboxControl ?? new SandboxControl(
        (configuration["ForwardFunding:StatePath"] ?? Path.GetFullPath("data/forward-funding-v1.json")) + ".control.json",
        configuration["WebOrigin"] ?? "http://localhost:3000");
    private readonly SemaphoreSlim gate = new(1, 1);
    private string status = "NOT_STARTED";
    private string? failure;
    private DateOnly? lastMeeting;

    public ForwardPaperCompanyStatus GetStatus()
    {
        DailyMeetingAttempt? latest = null;
        var effectiveStatus = status;
        var effectiveError = failure;
        var paused = false;
        try
        {
            paused = control.Paused;
            latest = journal.Read(ForwardFundingCalendar.BangkokDate(clock.GetUtcNow()));
            // An active meeting owns the daily claim. A saved claim after restart is interrupted work.
            if (gate.CurrentCount != 0) journal.EnsureReconciled();
            if (effectiveStatus == "NOT_STARTED" && latest is { Status: not "STARTED" })
            {
                var account = paper.Read();
                effectiveStatus = account is not null && ForwardPaperLedger.Snapshot(account).PendingOrders.Count > 0
                    ? "AWAITING_EXECUTION_QUOTE" : latest.Status;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        { effectiveError = "Forward company journal unavailable. Inspect saved meeting records before restart."; effectiveStatus = "RECONCILIATION_REQUIRED"; }
        if (latest?.Status == "STARTED" && gate.CurrentCount != 0) effectiveStatus = "RECONCILIATION_REQUIRED";
        if (paused && effectiveError is null && gate.CurrentCount != 0) effectiveStatus = "PAUSED";
        return new(effectiveStatus, meeting.Enabled, meeting.Model, "forward-paper", latest?.BusinessDate ?? lastMeeting, quotes.SupportedSymbols,
            quotes is AlpacaForwardPaperQuotes alpaca ? alpaca.CredentialsConfigured : null,
            quotes is ForwardMarketQuotes or BitkubForwardPaperQuotes,
            quotes is ForwardMarketQuotes markets ? markets.EquityCredentialsConfigured : null,
            effectiveError, "After 09:00 Asia/Bangkok; crypto daily, equities weekdays; execution checked every 30 seconds",
            "Crypto: observed spread + simulated 0.25% fee per side; limited to quoted depth. Equities: spread only. Taxes not modeled.", latest, paused, gate.CurrentCount == 0);
    }

    public bool SetPaused(bool paused)
    {
        // Resume cannot clear a failed or interrupted account. Inspection/restart stays required.
        if (GetStatus().Error is not null) return false;
        try
        {
            control.SetPaused(paused);
            if (!paused && status == "PAUSED") status = "NOT_STARTED";
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        { logger.LogError(ex, "Sandbox control unavailable."); return false; }
    }

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            if (failure is not null) return;
            if (control.Paused) { status = "PAUSED"; return; }
            if (!configuration.GetValue<bool>("ForwardFunding:Enabled")) { status = "DISABLED"; return; }
            var now = clock.GetUtcNow();
            journal.EnsureReconciled();
            var treasury = funding.Advance(now);
            var archive = legacy.Read();
            var account = paper.Update(current => ForwardPaperFunding.Synchronize(current, treasury, archive, now,
                configuration.GetValue<decimal>("ForwardFunding:InitialPaperCapital")));
            if (!meeting.Enabled) { status = "MODEL_DISABLED"; return; }
            if (ForwardPaperLedger.Snapshot(account).TotalContributed == 0) { status = "AWAITING_INVESTMENT_BUDGET"; return; }

            // Every pending execution is checked against the current journal inside its disk lease.
            var before = ForwardPaperLedger.Snapshot(account);
            var symbols = before.PendingOrders.Select(o => o.Symbol).Concat(before.Positions.Select(p => p.Symbol.Value)).Distinct().ToArray();
            var observedQuotes = new Dictionary<string, ForwardPaperQuote>();
            foreach (var symbol in symbols)
            {
                var quote = await quotes.GetAsync(symbol, clock.GetUtcNow(), cancellationToken);
                if (quote is null) continue;
                observedQuotes[symbol] = quote;
            }
            if (observedQuotes.Count > 0)
                paper.Update(current =>
                {
                    if (current is null) throw new InvalidDataException("Forward paper account disappeared.");
                    ForwardPaperFunding.Pending(current, treasury);
                    foreach (var quote in observedQuotes.Values)
                        current = ForwardPaperLedger.Mark(current, quote, clock.GetUtcNow());
                    var snapshot = ForwardPaperLedger.Snapshot(current);
                    foreach (var order in snapshot.PendingOrders)
                        if (observedQuotes.TryGetValue(order.Symbol, out var quote))
                            current = ForwardPaperLedger.Execute(current, order.Id, quote, clock.GetUtcNow());
                    return current;
                });

            now = clock.GetUtcNow();
            var local = now.ToOffset(TimeSpan.FromHours(7));
            var day = DateOnly.FromDateTime(local.DateTime);
            var prior = journal.Read(day);
            if (prior is not null)
            {
                lastMeeting = day;
                status = ForwardPaperLedger.Snapshot(paper.Read()!).PendingOrders.Count > 0 ? "AWAITING_EXECUTION_QUOTE" : prior.Status;
                return;
            }
            var weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var researchSymbols = quotes.SupportedSymbols.Where(s => !weekend ||
                s.EndsWith("-THB", StringComparison.Ordinal) || s.EndsWith("-USDT", StringComparison.Ordinal)).ToArray();
            if (local.Hour < 9 || researchSymbols.Length == 0) { status = "WAITING_FOR_WORKDAY"; return; }
            var attempt = Guid.NewGuid();
            if (!journal.TryBegin(day, attempt)) { status = "ALREADY_ATTEMPTED"; return; }
            lastMeeting = day;
            status = "RESEARCHING";
            var cutoff = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
            var candidates = new List<AiCompanyCandidateEvidence>();
            var starting = ForwardPaperLedger.Snapshot(paper.Read()!);
            foreach (var symbol in researchSymbols)
            {
                try
                {
                    var instrument = new Symbol(symbol);
                    var bars = (await history.GetAsync(instrument, cutoff.AddYears(-1), cutoff, cancellationToken))
                        .Where(b => b.Symbol == instrument && b.Date <= cutoff && b.Close > 0).OrderBy(b => b.Date).ToArray();
                    if (bars.Length < 200 || cutoff.DayNumber - bars[^1].Date.DayNumber > 4) continue;
                    var forecast = DeterministicForecaster.Evaluate(bars, instrument);
                    var held = starting.Positions.FirstOrDefault(p => p.Symbol == instrument);
                    candidates.Add(new($"{symbol}:{forecast.AsOf:yyyy-MM-dd}", instrument, forecast.AsOf, bars[^1].Close,
                        held is not null, held?.Quantity ?? 0, held?.UnrealizedPnl ?? 0, forecast.Score, forecast.Direction,
                        StrategyKind.Cash, false, 0, 1));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { logger.LogWarning(ex, "Forward research unavailable for {Symbol}.", symbol); }
            }
            if (candidates.Count == 0) { journal.Complete(day, attempt, "NO_EVIDENCE"); status = "NO_EVIDENCE"; return; }
            var input = new AiCompanyMeetingInput(cutoff,
                Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(candidates))),
                new(starting.Equity, starting.Cash, starting.Positions.Sum(p => p.MarketValue), 0, starting.Drawdown, starting.Positions.Count),
                candidates, "Forward paper company. Only owner-allocated Sandbox capital and CFO-approved salary investments. No leverage. HOLD is valid. " +
                    "No news evidence or promoted strategy validation is supplied. AI proposes; RiskEngine decides. " +
                    "Orders await fresh post-decision quotes; no retrospective fills.", .20m, 4);
            status = "MEETING";
            var result = await meeting.RunAsync(input, cancellationToken);
            if (result.Status == "COMPLETED")
            {
                var decisionAt = clock.GetUtcNow();
                // Commit the entire batch together. The daily claim is completed only after that commit.
                paper.Update(current =>
                {
                    if (current is null) throw new InvalidDataException("Missing account at proposal commit.");
                    var snapshot = ForwardPaperLedger.Snapshot(current);
                    if (result.Proposals.Select(p => p.Symbol).Distinct().Count() != result.Proposals.Count)
                        throw new InvalidDataException("Duplicate proposal symbols.");
                    foreach (var proposal in result.Proposals)
                    {
                        var evidence = candidates.Single(c => c.Symbol == proposal.Symbol);
                        if (snapshot.PendingOrders.Any(o => o.Symbol == proposal.Symbol.Value)) continue;
                        if (proposal.TargetWeight is < 0 or > .20m || proposal.Confidence is < 0 or > 1)
                            throw new InvalidDataException("Invalid final proposal limits.");
                        var held = snapshot.Positions.FirstOrDefault(p => p.Symbol == proposal.Symbol);
                        var intent = AiCompanyProposalSizer.Size(proposal, held, evidence.LastPrice, snapshot.Equity);
                        if (intent is null) continue;
                        var id = new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{attempt}:{proposal.Symbol.Value}"))[..16]);
                        current = ForwardPaperLedger.Submit(current, new(id, proposal.Symbol.Value, intent.Side, intent.Quantity, decisionAt, new()), decisionAt);
                    }
                    return current;
                });
            }
            journal.Complete(day, attempt, result.Status, Summarize(result));
            status = ForwardPaperLedger.Snapshot(paper.Read()!).PendingOrders.Count > 0 ? "AWAITING_EXECUTION_QUOTE" : result.Status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            failure = "Forward company halted. Inspect saved funding, meeting and execution records before restart.";
            status = "RECONCILIATION_REQUIRED";
            logger.LogError(ex, "Forward paper company halted.");
        }
        finally { gate.Release(); }
    }

    private static DailyMeetingSummary Summarize(AiCompanyMeetingResult result)
    {
        var reports = new List<SavedAiReport>();
        foreach (var artifact in result.Status == "COMPLETED" ? result.Artifacts : [])
        {
            using var output = JsonDocument.Parse(artifact.OutputJson);
            if (output.RootElement.ValueKind != JsonValueKind.Object ||
                !output.RootElement.TryGetProperty("summary", out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString())) continue;
            reports.Add(new(artifact.Key.Role, value.GetString()!, artifact.ProviderModel, artifact.InputTokens,
                artifact.OutputTokens, artifact.ArtifactId.ToString(), artifact.Key.EvidenceHash, artifact.Key.DataCutoff));
        }
        return new(result.ModelCalls, result.InputTokens, result.OutputTokens, reports);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        do { await TickAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
