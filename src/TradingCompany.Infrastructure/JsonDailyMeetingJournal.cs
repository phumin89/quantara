using TradingCompany.Application;
using TradingCompany.Domain;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingCompany.Infrastructure;

public sealed class JsonDailyMeetingJournal : IDailyMeetingJournal
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private sealed record Envelope(string Hash, DailyMeetingAttempt[] Attempts);
    private readonly string path;

    public TradingAccountContext? AccountContext { get; }

    public JsonDailyMeetingJournal(string path, TradingAccountContext? accountContext = null)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Journal path must be absolute.");
        if (!TradingAccountContext.IsValid(accountContext)) throw new ArgumentException("Invalid meeting account context.");
        this.path = Path.GetFullPath(path);
        AccountContext = accountContext;
    }

    public DailyMeetingAttempt? Read(DateOnly businessDate)
    {
        if (businessDate == default) throw new ArgumentException("Business date is required.");
        return Load(false).SingleOrDefault(x => x.BusinessDate == businessDate && x.ReviewHour is null);
    }

    public DailyMeetingAttempt? Read(DateOnly businessDate, int reviewHour)
    {
        if (businessDate == default) throw new ArgumentException("Business date is required.");
        ValidateHour(reviewHour);
        return Load(false).SingleOrDefault(x => x.BusinessDate == businessDate && x.ReviewHour == reviewHour);
    }

    public IReadOnlyList<DailyMeetingAttempt> ReadAll() => Load(false);

    public void EnsureReconciled()
    {
        if (Load(false).Any(x => x.Status == "STARTED"))
            throw new InvalidDataException("An interrupted meeting requires reconciliation before execution.");
    }

    public bool TryBegin(DateOnly businessDate, Guid attemptId)
        => TryBegin(businessDate, attemptId, null, null);

    public bool TryBegin(DateOnly businessDate, Guid attemptId, int? reviewHour, LiveSnapshot? reviewSnapshot)
    {
        if (businessDate == default || attemptId == Guid.Empty) throw new ArgumentException("Meeting identity required.");
        ValidateReview(reviewHour, reviewSnapshot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var lease = Acquire(out var created);
        var attempts = Load(created);
        if (attempts.Any(x => x.AttemptId == attemptId)) return false;
        if (attempts.Any(x => x.BusinessDate == businessDate && x.ReviewHour == reviewHour)) return false;
        if (attempts.Any(x => x.BusinessDate > businessDate)) throw new InvalidOperationException("Clock moved behind recorded meetings.");
        if (reviewHour is { } currentHour && attempts.Any(x => x.BusinessDate == businessDate && x.ReviewHour is { } priorHour && priorHour > currentHour))
            throw new InvalidOperationException("Review clock moved behind recorded meetings.");
        if (reviewSnapshot is not null && attempts.Any(x => x.ReviewSnapshot is { } previous && previous.ObservedAt > reviewSnapshot.ObservedAt))
            throw new InvalidOperationException("Review clock moved behind recorded snapshots.");
        if (attempts.Any(x => x.Status == "STARTED")) throw new InvalidDataException("An interrupted meeting requires reconciliation.");
        Save([.. attempts, new DailyMeetingAttempt(businessDate, attemptId, "STARTED", ReviewHour: reviewHour, ReviewSnapshot: reviewSnapshot) { AccountContext = AccountContext }]);
        return true;
    }

    public void Complete(DateOnly businessDate, Guid attemptId, string status, DailyMeetingSummary? summary = null)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Length > 100 || status == "STARTED")
            throw new ArgumentException("Terminal meeting status required.");
        ValidateSummary(summary);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var lease = Acquire(out _);
        var attempts = Load(false);
        var index = Array.FindIndex(attempts, x => x.BusinessDate == businessDate && x.AttemptId == attemptId);
        if (index < 0 || attempts[index].Status != "STARTED")
            throw new InvalidOperationException("Only the claimed attempt can finish once.");
        attempts[index] = attempts[index] with { Status = status, Summary = summary };
        Save(attempts);
    }

    private DailyMeetingAttempt[] Load(bool initializing)
    {
        if (!File.Exists(path))
        {
            if (!initializing && File.Exists(path + ".lock")) throw new InvalidDataException("Meeting journal missing; refusing to reset.");
            return [];
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var envelope = JsonSerializer.Deserialize<Envelope>(stream, Json);
            if (envelope?.Attempts is null || envelope.Hash != Hash(envelope.Attempts)) throw new InvalidDataException("Journal checksum failed.");
            var attempts = envelope.Attempts;
            if (attempts.Any(x => x is null || x.BusinessDate == default || x.AttemptId == Guid.Empty || string.IsNullOrWhiteSpace(x.Status)) ||
                attempts.Any(x => x.ReviewHour is < 0 or > 23) ||
                attempts.GroupBy(x => (x.BusinessDate, x.ReviewHour)).Any(g => g.Count() > 1) ||
                attempts.Select(x => x.AttemptId).Distinct().Count() != attempts.Length)
                throw new InvalidDataException("Journal contains invalid meeting identities.");
            foreach (var attempt in attempts)
            {
                if (attempt.AccountContext != AccountContext) throw new InvalidDataException("Meeting journal belongs to a different account.");
                ValidateReview(attempt.ReviewHour, attempt.ReviewSnapshot);
                ValidateSummary(attempt.Summary);
            }
            return attempts;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Meeting journal unreadable; no automatic recovery.", ex); }
    }

    private void Save(DailyMeetingAttempt[] attempts)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Envelope(Hash(attempts), attempts), Json);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private FileStream Acquire(out bool created)
    {
        try
        {
            var lease = new FileStream(path + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true;
            return lease;
        }
        catch (IOException) when (File.Exists(path + ".lock")) { }
        created = false;
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            try { return new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < deadline) { Thread.Sleep(20); }
        }
    }

    private static string Hash(DailyMeetingAttempt[] attempts) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(attempts, Json)));

    private void ValidateReview(int? hour, LiveSnapshot? snapshot)
    {
        if (hour is null && snapshot is null) return;
        if (AccountContext?.Mode != TradingMode.Live) throw new InvalidOperationException("Review meetings require a live account context.");
        if (hour is { } value) ValidateHour(value);
        if (snapshot is not null && snapshot.AccountId.Value.ToString("N") != AccountContext.AccountId)
            throw new ArgumentException("Review snapshot belongs to a different account.");
    }

    private static void ValidateSummary(DailyMeetingSummary? summary)
    {
        if (summary is null) return;
        if (!LiveMeetingIssues.IsValid(summary.Issues)) throw new InvalidDataException("Invalid meeting issues.");
        if (summary.Research is { } research && !CoinResearchValidation.IsValid(research, research.ObservedAt))
            throw new InvalidDataException("Invalid research evidence.");
        if (summary.ModelCalls < 0 || summary.InputTokens < 0 || summary.OutputTokens < 0 ||
            summary.Reports is null || summary.Reports.Count > 100)
            throw new InvalidDataException("Meeting summary contains invalid counts.");
        foreach (var report in summary.Reports)
        {
            if (report is null || report.InputTokens < 0 || report.OutputTokens < 0 ||
                string.IsNullOrWhiteSpace(report.Role) || report.Role.Length > 100 ||
                report.Summary is null || report.Summary.Length > 100_000 ||
                string.IsNullOrWhiteSpace(report.ProviderModel) || report.ProviderModel.Length > 200 ||
                string.IsNullOrWhiteSpace(report.ArtifactId) || report.ArtifactId.Length > 500 ||
                string.IsNullOrWhiteSpace(report.EvidenceHash) || report.EvidenceHash.Length > 500 ||
                report.DataCutoff == default)
                throw new InvalidDataException("Meeting summary contains invalid report data.");
        }
    }

    private static void ValidateHour(int reviewHour)
    {
        if (reviewHour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(reviewHour));
    }
}
