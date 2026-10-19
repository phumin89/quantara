using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class CodexWorker : ICodexWorker
{
    public const int MaxRequestCharacters = 100000;

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string nodePath;

    private readonly string scriptPath;

    private readonly string? authHome;

    private readonly string? launcherPath;

    public CodexWorker(string nodePath, string scriptPath, string? authHome = null, string? launcherPath = null)
    {
        this.nodePath = nodePath;
        this.scriptPath = scriptPath;
        this.authHome = authHome;
        this.launcherPath = launcherPath;
    }

    public bool IsConfigured => File.Exists(this.nodePath) && File.Exists(this.scriptPath) && this.HasAuthentication();

    private static string? Key =>
        Environment.GetEnvironmentVariable("CODEX_API_KEY") ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public async Task<string> Run(
        string role,
        string objective,
        IReadOnlyList<(string Role, string Output)> previous,
        string? portfolio,
        CancellationToken cancellationToken
    ) => (await this.Report(role, objective, previous, portfolio, cancellationToken)).Output;

    public async Task<ResearchReport> Report(
        string role,
        string objective,
        IReadOnlyList<(string Role, string Output)> previous,
        string? portfolio,
        CancellationToken cancellationToken
    )
    {
        if (!this.IsConfigured)
        {
            throw new TeamUnavailableException();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(180));
        var start = new ProcessStartInfo(this.nodePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(this.scriptPath);
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                start.Environment[name] = value;
            }
        }

        if (this.authHome is not null)
        {
            start.Environment["QUANTARA_CODEX_AUTH_HOME"] = this.authHome;
            start.Environment["QUANTARA_CODEX_LAUNCHER"] = this.launcherPath;
        }
        else
        {
            start.Environment["CODEX_API_KEY"] = CodexWorker.Key;
        }

        var body = CodexWorker.SerializeRequest(role, objective, previous, portfolio);
        if (body.Length > CodexWorker.MaxRequestCharacters)
        {
            throw new CodexWorkerException("worker_input_too_large");
        }

        timeout.Token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        process.Start();
        var output = CodexWorker.ReadBounded(process.StandardOutput, timeout.Token);
        var stderr = CodexWorker.ReadBounded(process.StandardError, timeout.Token);
        try
        {
            await process.StandardInput.WriteAsync(body.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            var pending = new List<Task> { process.WaitForExitAsync(timeout.Token), output, stderr };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }

            var diagnostic = await stderr;
            var json = await output;
            if (process.ExitCode != 0)
            {
                throw CodexWorkerException.FromDiagnostic(diagnostic);
            }

            try
            {
                var report = CodexWorker.Decode(json, role);
                if (role == "reviewer" && report.Assessment is null && portfolio?.StartsWith('{') == true)
                {
                    using var context = JsonDocument.Parse(portfolio);
                    if (context.RootElement.TryGetProperty("assessmentContext", out _))
                    {
                        throw new CodexWorkerException("worker_response_invalid");
                    }
                }

                return report;
            }
            catch (Exception error)
                when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                throw new CodexWorkerException("worker_response_invalid");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CodexWorkerException("worker_timed_out");
        }
        finally
        {
            // Cancellation or an invalid pipe must not leave an isolated worker running.
            await timeout.CancelAsync();
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
            }

            await process
                .WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            try
            {
                await Task.WhenAll(output, stderr);
            }
            catch (Exception error)
                when (error is OperationCanceledException or IOException or InvalidOperationException)
            {
                // The primary read/exit failure is already propagated above; observe both cleanup tasks.
            }
        }
    }

    public static string SerializeRequest(
        string role,
        string objective,
        IReadOnlyList<(string Role, string Output)> previous,
        string? portfolio
    ) =>
        JsonSerializer.Serialize(
            new
            {
                role,
                objective,
                previous = previous.Select(item => new { role = item.Role, output = item.Output }),
                portfolio,
            },
            CodexWorker.jsonOptions
        );

    private static ResearchReport Decode(string json, string role)
    {
        using var result = JsonDocument.Parse(json);
        var report = result.RootElement.GetProperty("output").GetString();
        if (string.IsNullOrWhiteSpace(report) || report.Length > 16000)
        {
            throw new InvalidOperationException("Codex returned an invalid report.");
        }

        TradeProposal? proposal = null;
        if (
            result.RootElement.TryGetProperty("proposal", out var candidate)
            && candidate.ValueKind != JsonValueKind.Null
        )
        {
            if (role != "reviewer")
            {
                throw new InvalidOperationException("Only Reviewer can produce a pending proposal.");
            }

            var side = candidate.GetProperty("side").GetString() switch
            {
                "buy" => Quantara.Team.Domain.TradeProposalSide.Buy,
                "sell" => Quantara.Team.Domain.TradeProposalSide.Sell,
                _ => throw new InvalidOperationException("Invalid proposal side."),
            };
            TradeEntryPlan? entryPlan = null;
            if (candidate.TryGetProperty("entryPlan", out var plan) && plan.ValueKind != JsonValueKind.Null)
            {
                entryPlan = new TradeEntryPlan(
                    plan.GetProperty("targetPrice").GetString() ?? "",
                    plan.GetProperty("invalidationPrice").GetString() ?? "",
                    plan.GetProperty("reviewAfterMinutes").GetInt32()
                );
            }

            proposal = new TradeProposal(
                candidate.GetProperty("symbol").GetString() ?? "",
                side,
                candidate.GetProperty("amount").GetString() ?? "",
                candidate.GetProperty("limitPrice").GetString() ?? "",
                candidate.GetProperty("reason").GetString() ?? "",
                candidate.GetProperty("evidenceAt").GetDateTimeOffset(),
                entryPlan
            );
        }

        LearningLesson? lesson = null;
        if (!result.RootElement.TryGetProperty("lesson", out var lessonElement))
        {
            throw new InvalidOperationException("Worker must return a learning lesson field.");
        }
        else if (lessonElement.ValueKind != JsonValueKind.Null)
        {
            if (role != "reviewer" || lessonElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Invalid learning lesson.");
            }

            var properties = lessonElement
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray();
            if (!properties.SequenceEqual(new[] { "evidence", "lesson", "nextTest" }))
            {
                throw new InvalidOperationException("Invalid learning lesson.");
            }

            var lessonText = CodexWorker.LessonText(lessonElement, "lesson");
            var evidenceText = CodexWorker.LessonText(lessonElement, "evidence");
            var nextTestText = CodexWorker.LessonText(lessonElement, "nextTest");
            lesson = new LearningLesson(lessonText, evidenceText, nextTestText);
        }
        else if (role == "reviewer")
        {
            throw new InvalidOperationException("Reviewer must return a learning lesson.");
        }

        var researchSymbols = result.RootElement.TryGetProperty("researchSymbols", out var requested)
            ? requested.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
            : [];
        if (
            researchSymbols.Length > 3
            || (researchSymbols.Length > 0 && role != "ceo")
            || researchSymbols.Distinct(StringComparer.Ordinal).Count() != researchSymbols.Length
            || researchSymbols.Any(symbol => symbol.Length is < 1 or > 30 || !symbol.All(char.IsAsciiLetterOrDigit))
        )
        {
            throw new CodexWorkerException("worker_response_invalid");
        }

        ResearchAssessment? assessment = null;
        if (
            result.RootElement.TryGetProperty("assessment", out var assessmentElement)
            && assessmentElement.ValueKind != JsonValueKind.Null
        )
        {
            if (assessmentElement.ValueKind != JsonValueKind.Object || role != "reviewer")
            {
                throw new CodexWorkerException("worker_response_invalid");
            }

            var schemaVersion = assessmentElement.GetProperty("schemaVersion").GetString();
            var accountKey = assessmentElement.GetProperty("accountKey").GetString();
            var runId = assessmentElement.GetProperty("runId").GetGuid();
            var asOf = assessmentElement.GetProperty("asOf").GetDateTimeOffset();
            var expiresAt = assessmentElement.GetProperty("expiresAt").GetDateTimeOffset();
            if (schemaVersion != "research-assessment-v1" || string.IsNullOrWhiteSpace(accountKey) || expiresAt <= asOf)
            {
                throw new CodexWorkerException("worker_response_invalid");
            }

            assessment = new ResearchAssessment(
                schemaVersion,
                accountKey,
                runId,
                assessmentElement.TryGetProperty("policyHash", out var policy) ? policy.GetString() : null,
                asOf,
                expiresAt,
                assessmentElement
                    .GetProperty("evidenceIds")
                    .EnumerateArray()
                    .Select(item => item.GetString() ?? "")
                    .ToArray(),
                assessmentElement
                    .GetProperty("positions")
                    .EnumerateArray()
                    .Select(item => new PositionAssessment(
                        item.GetProperty("symbol").GetString() ?? "",
                        item.GetProperty("action").GetString() ?? "",
                        item.GetProperty("thesis").GetString() ?? "",
                        item.GetProperty("reason").GetString() ?? "",
                        item.GetProperty("reviewAfter").GetDateTimeOffset(),
                        item.GetProperty("expiresAt").GetDateTimeOffset(),
                        item.GetProperty("missingFields")
                            .EnumerateArray()
                            .Select(field => field.GetString() ?? "")
                            .ToArray()
                    ))
                    .ToArray(),
                assessmentElement
                    .GetProperty("opportunities")
                    .EnumerateArray()
                    .Select(item => new ResearchOpportunity(
                        item.GetProperty("symbol").GetString() ?? "",
                        item.GetProperty("reason").GetString() ?? "",
                        item.GetProperty("reconsiderWhen").GetString() ?? "",
                        item.GetProperty("reviewAfter").GetDateTimeOffset()
                    ))
                    .ToArray()
            );
        }

        return new ResearchReport(report, proposal, lesson, researchSymbols, assessment);
    }

    private static string LessonText(JsonElement lesson, string name)
    {
        var value = lesson.GetProperty(name).GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500)
        {
            throw new InvalidOperationException("Invalid learning lesson.");
        }

        return value;
    }

    private bool HasAuthentication()
    {
        if (this.authHome is null)
        {
            return !string.IsNullOrWhiteSpace(CodexWorker.Key);
        }

        if (!File.Exists(this.launcherPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(this.authHome, "auth.json")));

            return document.RootElement.TryGetProperty("auth_mode", out var mode)
                && mode.GetString() == "chatgpt"
                && document.RootElement.TryGetProperty("tokens", out var tokens)
                && tokens.ValueKind == JsonValueKind.Object
                && tokens.TryGetProperty("access_token", out var token)
                && !string.IsNullOrWhiteSpace(token.GetString());
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            output.Append(buffer, 0, count);
            if (output.Length > 100000)
            {
                throw new InvalidOperationException("Codex returned too much output.");
            }
        }

        return output.ToString();
    }
}
