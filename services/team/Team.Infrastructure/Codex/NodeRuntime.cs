namespace Quantara.Team.Infrastructure;

public static class NodeRuntime
{
    public static string Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory.Trim('"'), executable);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return "";
    }
}
