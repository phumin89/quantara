using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakeCodexWorker : ICodexWorker
{
    private readonly Func<string, IReadOnlyList<(string Role, string Output)>, CancellationToken, Task<string>> execute;

    public FakeCodexWorker(
        Func<string, IReadOnlyList<(string Role, string Output)>, CancellationToken, Task<string>> execute,
        bool configured = true
    )
    {
        this.execute = execute;
        this.IsConfigured = configured;
    }

    public bool IsConfigured { get; }

    public List<(
        string Role,
        string Objective,
        IReadOnlyList<(string Role, string Output)> Previous,
        string? Portfolio
    )> Calls { get; } = [];

    public Task<string> Run(
        string role,
        string objective,
        IReadOnlyList<(string Role, string Output)> previous,
        string? portfolio,
        CancellationToken cancellationToken
    )
    {
        this.Calls.Add((role, objective, previous.ToArray(), portfolio));
        return this.execute(role, previous, cancellationToken);
    }
}
