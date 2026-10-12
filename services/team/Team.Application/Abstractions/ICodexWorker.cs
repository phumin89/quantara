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
}
