namespace Quantara.Portfolio.Application;

public interface IHumanOrderReview
{
    public Task<OrderView> Review(Guid orderId, bool approve, CancellationToken cancellationToken);
}
