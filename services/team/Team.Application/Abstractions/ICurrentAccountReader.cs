namespace Quantara.Team.Application;

public interface ICurrentAccountReader
{
    public Task<string> Read(CancellationToken cancellationToken);
}
