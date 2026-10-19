using Mediator;

namespace Quantara.Team.Application;

public sealed class GetTeamNewsHandler(INewsEvidenceReader reader) : IQueryHandler<GetTeamNewsQuery, string>
{
    public ValueTask<string> Handle(GetTeamNewsQuery query, CancellationToken cancellationToken) =>
        new(this.Read(query.Symbols, cancellationToken));

    private async Task<string> Read(string? symbols, CancellationToken cancellationToken) =>
        await reader.Read(symbols, cancellationToken);
}
