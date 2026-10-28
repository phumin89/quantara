using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Team.Checks;

public sealed class FakeCodexWorker : ICodexWorker
{
    private readonly Func<string, IReadOnlyList<(string Role, string Output)>, CancellationToken, Task<string>> execute;

    private readonly Func<
        string,
        IReadOnlyList<(string Role, string Output)>,
        CancellationToken,
        Task<ResearchReport>
    >? report;

    public FakeCodexWorker(
        Func<string, IReadOnlyList<(string Role, string Output)>, CancellationToken, Task<string>> execute,
        bool configured = true
    )
    {
        this.execute = execute;
        this.IsConfigured = configured;
    }

    public FakeCodexWorker(
        Func<string, IReadOnlyList<(string Role, string Output)>, CancellationToken, Task<ResearchReport>> report,
        bool configured = true
    )
        : this(
            (role, previous, cancellationToken) =>
                report(role, previous, cancellationToken).ContinueWith(result => result.Result.Output),
            configured
        )
    {
        this.report = report;
    }

    public bool IsConfigured { get; }

    public Func<string, ResearchModelAudit?>? SelectModel { get; init; }

    public ResearchModelAudit? ModelSelection(string role) => this.SelectModel?.Invoke(role);

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

    public Task<ResearchReport> Report(
        string role,
        string objective,
        IReadOnlyList<(string Role, string Output)> previous,
        string? portfolio,
        CancellationToken cancellationToken
    )
    {
        this.Calls.Add((role, objective, previous.ToArray(), portfolio));
        return this.report is null
            ? Task.FromResult(
                new ResearchReport(this.execute(role, previous, cancellationToken).GetAwaiter().GetResult(), null)
            )
            : this.report(role, previous, cancellationToken);
    }
}
