using System.Diagnostics;

namespace Quantara.Team.Infrastructure;

public static class CodexLauncher
{
    public static async Task<int> Run(string[] arguments)
    {
        var executable = Environment.GetEnvironmentVariable("QUANTARA_CODEX_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            return 1;
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--ignore-user-config");
        start.ArgumentList.Add("--ephemeral");
        foreach (var argument in arguments.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        var input = Task.Run(() => CodexLauncher.ForwardInput(process, cancellation.Token));
        var output = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var errors = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        await process.WaitForExitAsync();
        await cancellation.CancelAsync();
        await Task.WhenAll(output, errors);
        try
        {
            await input.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException) { }

        return process.ExitCode;
    }

    private static async Task ForwardInput(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await Console.OpenStandardInput().CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
            process.StandardInput.Close();
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
}
