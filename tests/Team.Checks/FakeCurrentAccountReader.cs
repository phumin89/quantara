using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakeCurrentAccountReader : ICurrentAccountReader
{
    private readonly string accountKey;

    public FakeCurrentAccountReader(string accountKey) => this.accountKey = accountKey;

    public Task<string> Read(CancellationToken cancellationToken) => Task.FromResult(this.accountKey);
}
