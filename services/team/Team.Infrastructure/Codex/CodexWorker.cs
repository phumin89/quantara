using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public sealed class CodexWorker : ICodexWorker
{
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

        using var process = new Process { StartInfo = start };
        process.Start();
        using var kill = timeout.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) { }
        });
        var output = CodexWorker.ReadBounded(process.StandardOutput, timeout.Token);
        var stderr = CodexWorker.ReadBounded(process.StandardError, timeout.Token);
        var body = JsonSerializer.Serialize(
            new
            {
                role,
                objective,
                previous = previous.Select(item => new { role = item.Role, output = item.Output }),
                portfolio,
            },
            CodexWorker.jsonOptions
        );
        try
        {
            await process.StandardInput.WriteAsync(body.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var diagnostic = await stderr;
            var json = await output;
            if (process.ExitCode != 0)
            {
                throw CodexWorkerException.FromDiagnostic(diagnostic);
            }

            try
            {
                return CodexWorker.Decode(json, role);
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
    }

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

        return new ResearchReport(report, proposal, lesson, researchSymbols);
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
