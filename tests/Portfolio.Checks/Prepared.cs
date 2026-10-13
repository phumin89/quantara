using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class Prepared(FakeOrderBroker broker) : IPreparedOrderSubmission
{
    public void Dispose() { }

    public async Task<OrderBrokerResult> Send(CancellationToken cancellationToken)
    {
        broker.CountSubmission();
        if (broker.FailSubmit)
        {
            throw new HttpRequestException("Fixture uncertain result");
        }

        if (broker.Release is not null)
        {
            await broker.Release.Task.WaitAsync(cancellationToken);
        }

        return broker.Rejected ? new(false, null, "Rejected fixture") : new(true, "broker-123", "Accepted, not filled");
    }
}
