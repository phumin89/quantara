namespace Quantara.Team.Application;

public interface ICodexWorker
{
    public Task<string> Run(
        string role,
        string objective,
        IReadOnlyList<(string Role, string Output)> previous,
        string? portfolio,
        CancellationToken cancellationToken
    );

    public bool IsConfigured { get; }

    public async Task<ResearchReport> Report(
        string role,
        string objective,
        IReadOnlyList<(string Role, string Output)> previous,
        string? portfolio,
        CancellationToken cancellationToken
    ) => new(await this.Run(role, objective, previous, portfolio, cancellationToken), null);
}
