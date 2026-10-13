namespace Quantara.Portfolio.Application;

public interface IPreparedOrderSubmission : IDisposable
{
    public Task<OrderBrokerResult> Send(CancellationToken cancellationToken);
}
