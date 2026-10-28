using Quantara.Team.Application;

namespace Team.Checks;

public sealed class AdvancingAccountReader(string accountKey, Action beforeRead) : ICurrentAccountReader
{
    public int Reads { get; private set; }

    public Task<string> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Reads++;
        beforeRead();
        return Task.FromResult(accountKey);
    }
}
