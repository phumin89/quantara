using System.Diagnostics;
using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class WorkerProcessCleanupChecks
{
    public static async Task Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quantara-worker-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "wait.mjs");
        var pidFile = Path.Combine(directory, "pid.txt");
        var key = Environment.GetEnvironmentVariable("CODEX_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY", "fixture-only");
            await File.WriteAllTextAsync(
                script,
                "import { writeFileSync, renameSync } from 'node:fs'; const target = "
                    + JsonSerializer.Serialize(pidFile)
                    + "; writeFileSync(target + '.tmp', String(process.pid)); renameSync(target + '.tmp', target); setInterval(() => {}, 1000);"
            );
            var worker = new CodexWorker(NodeRuntime.Resolve(null), script);
            for (var iteration = 0; iteration < 8; iteration++)
            {
                File.Delete(pidFile);
                using var cancellation = new CancellationTokenSource();
                var pending = worker.Run("ceo", "fixture cancellation", [], null, cancellation.Token);
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(pidFile))
                {
                    if (deadline.Elapsed > TimeSpan.FromSeconds(5))
                    {
                        await cancellation.CancelAsync();
                        throw new InvalidOperationException("The fixture process did not start.");
                    }

                    await Task.Delay(5);
                }

                var pid = int.Parse(
                    await File.ReadAllTextAsync(pidFile),
                    System.Globalization.CultureInfo.InvariantCulture
                );
                using var child = Process.GetProcessById(pid);
                try
                {
                    await cancellation.CancelAsync();
                    try
                    {
                        await pending.WaitAsync(TimeSpan.FromSeconds(5));
                        throw new InvalidOperationException("A cancelled worker returned a report.");
                    }
                    catch (OperationCanceledException) { }

                    if (!child.HasExited)
                    {
                        throw new InvalidOperationException("Worker cancellation returned before its process exited.");
                    }
                }
                finally
                {
                    if (!child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }
            }

            File.Delete(pidFile);
            await File.AppendAllTextAsync(script, "process.stdout.write('x'.repeat(120000));");
            using var outputCancellation = new CancellationTokenSource();
            var oversized = worker.Run("ceo", "fixture oversized output", [], null, outputCancellation.Token);
            try
            {
                try
                {
                    await oversized.WaitAsync(TimeSpan.FromSeconds(3));
                    throw new InvalidOperationException("Oversized worker output was accepted.");
                }
                catch (InvalidOperationException error) when (error.Message == "Codex returned too much output.") { }

                var pid = int.Parse(
                    await File.ReadAllTextAsync(pidFile),
                    System.Globalization.CultureInfo.InvariantCulture
                );
                try
                {
                    using var child = Process.GetProcessById(pid);
                    if (!child.HasExited)
                    {
                        throw new InvalidOperationException("Output failure returned before its process exited.");
                    }
                }
                catch (ArgumentException) { }
            }
            finally
            {
                await outputCancellation.CancelAsync();
                try
                {
                    await oversized.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception error) when (error is OperationCanceledException or InvalidOperationException) { }
            }

            Console.WriteLine("Worker cleanup: cancellation and oversized output terminate without orphan fixtures.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY", key);
            File.Delete(pidFile);
            File.Delete(pidFile + ".tmp");
            File.Delete(script);
            Directory.Delete(directory);
        }
    }
}
