using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TradingCompany.Application;

namespace TradingCompany.Infrastructure;

public sealed record CodexCliProcessResult(int ExitCode, string Stdout, string Stderr);
public delegate Task<CodexCliProcessResult> CodexCliProcessRunner(IReadOnlyList<string> Arguments, string WorkingDirectory, string Input, CancellationToken CancellationToken);

public sealed class CodexCliTradingAgentGateway : ITradingAgentGateway
{
    private const int OutputLimit = 256 * 1024;
    private static readonly string[] AllowedEnvironment =
    [
        "APPDATA", "LOCALAPPDATA", "USERPROFILE", "HOME", "CODEX_HOME",
        "PATH", "PATHEXT", "SystemRoot", "WINDIR", "TEMP", "TMP",
        "COMSPEC", "HOMEDRIVE", "HOMEPATH", "USERNAME"
    ];
    private readonly string executablePath;
    private readonly CodexCliProcessRunner runner;

    public CodexCliTradingAgentGateway(string executablePath, CodexCliProcessRunner? runner = null)
    {
        this.executablePath = string.IsNullOrWhiteSpace(executablePath) ? throw new ArgumentException("Executable path is required.", nameof(executablePath)) : executablePath;
        this.runner = runner ?? RunProcessAsync;
    }

    public async Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Model)) throw new AgentModelUnavailableException("Codex Agent model is unavailable.");
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request.Timeout));
        ValidateJson(request.InputJson, nameof(request.InputJson)); ValidateJson(request.JsonSchema, nameof(request.JsonSchema));
        var temp = Path.Combine(Path.GetTempPath(), "quantara-codex-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var schemaPath = Path.Combine(temp, "schema.json"); await File.WriteAllTextAsync(schemaPath, request.JsonSchema, cancellationToken);
            var args = new List<string> { "exec", "--json", "--ephemeral", "--ignore-user-config", "--sandbox", "read-only", "--skip-git-repo-check", "-C", temp, "--output-schema", schemaPath };
            if (!string.Equals(request.Model, "codex-default", StringComparison.Ordinal)) { args.Add("-m"); args.Add(request.Model); }
            args.Add("-");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(request.Timeout);
            CodexCliProcessResult process;
            var prompt = $"You are the {request.Role} specialist advising a trading company. You cannot execute trades. " +
                $"If the output schema requires a role field, set it exactly \"{request.Role}\". " +
                "Copy any required dataCutoff, evidenceHash, and evidenceIds from the supplied input exactly; do not invent them. " +
                "Amounts are in THB. Return only the JSON object required by the output schema.\n" +
                request.SystemInstructions + "\nINPUT JSON:\n" + request.InputJson;
            try { process = await runner(args, temp, prompt, timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AgentProviderTimeoutException("Codex Agent provider request timed out."); }
            catch (OperationCanceledException) { throw; }
            catch { throw new AgentProviderException("Codex Agent provider process failed."); }
            if (process.ExitCode != 0) throw new AgentProviderException("Codex Agent provider process failed.");
            return Parse(process.Stdout);
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    private static AgentModelResponse Parse(string stdout)
    {
        string? output = null, model = null; var inputTokens = 0; var outputTokens = 0; var completed = false;
        try
        {
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var type)) continue;
                if (type.GetString() == "item.completed" && root.TryGetProperty("item", out var item) && item.TryGetProperty("type", out var itemType) && itemType.GetString() == "agent_message") output = item.GetProperty("text").GetString();
                if (type.GetString() == "turn.completed") { completed = true; var usage = root.GetProperty("usage"); inputTokens = usage.GetProperty("input_tokens").GetInt32(); outputTokens = usage.GetProperty("output_tokens").GetInt32(); if (root.TryGetProperty("model", out var m)) model = m.GetString(); }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { throw new AgentResponseFormatException("Codex Agent response was malformed."); }
        if (!completed || string.IsNullOrWhiteSpace(output)) throw new AgentResponseFormatException("Codex Agent response was incomplete.");
        try { using var outputDoc = JsonDocument.Parse(output); } catch (JsonException) { throw new AgentResponseFormatException("Codex Agent response was malformed."); }
        return new AgentModelResponse(output, "codex-cli", model ?? "codex", inputTokens, outputTokens, TimeSpan.Zero);
    }

    private static void ValidateJson(string value, string name) { try { using var _ = JsonDocument.Parse(value); } catch (JsonException) { throw new ArgumentException("Value must contain valid JSON.", name); } }

    private async Task<CodexCliProcessResult> RunProcessAsync(IReadOnlyList<string> args, string workingDirectory, string input, CancellationToken token)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = executablePath, WorkingDirectory = workingDirectory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment.Clear();
        foreach (var key in AllowedEnvironment)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(value)) process.StartInfo.Environment[key] = value;
        }
        process.Start();
        var stdout = ReadBoundedAsync(process.StandardOutput); var stderr = ReadBoundedAsync(process.StandardError);
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[8192]; var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer); if (read == 0) break;
            if (result.Length < OutputLimit)
                result.Append(buffer, 0, Math.Min(read, OutputLimit - result.Length));
        }
        return result.ToString();
    }
}
