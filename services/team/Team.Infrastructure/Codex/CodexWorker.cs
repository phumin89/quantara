using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class CodexWorker : ICodexWorker
{
    private readonly string nodePath;

    private readonly string scriptPath;

    public CodexWorker(string nodePath, string scriptPath)
    {
        this.nodePath = nodePath;
        this.scriptPath = scriptPath;
    }

    public bool IsConfigured =>
        File.Exists(this.nodePath) && File.Exists(this.scriptPath) && !string.IsNullOrWhiteSpace(CodexWorker.Key);

    private static string? Key =>
        Environment.GetEnvironmentVariable("CODEX_API_KEY") ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public async Task<string> Run(
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

        start.Environment["CODEX_API_KEY"] = CodexWorker.Key;
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
        var stderr = CodexWorker.Drain(process.StandardError, timeout.Token);
        var body = JsonSerializer.Serialize(
            new
            {
                role,
                objective,
                previous = previous.Select(item => new { role = item.Role, output = item.Output }),
                portfolio,
            }
        );
        await process.StandardInput.WriteAsync(body.AsMemory(), timeout.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        await stderr;
        var json = await output;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Codex worker failed. Check server authentication and runtime.");
        }

        using var result = JsonDocument.Parse(json);
        var report = result.RootElement.GetProperty("output").GetString();
        if (string.IsNullOrWhiteSpace(report) || report.Length > 16000)
        {
            throw new InvalidOperationException("Codex returned an invalid report.");
        }

        return report;
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

    private static async Task Drain(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) > 0) { }
    }
}
