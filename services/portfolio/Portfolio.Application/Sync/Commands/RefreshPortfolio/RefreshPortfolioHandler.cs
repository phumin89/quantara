using Mediator;
namespace Quantara.Portfolio.Application;

public sealed class RefreshPortfolioHandler(IPortfolioSync sync) : ICommandHandler<RefreshPortfolioCommand, bool>
{
    public ValueTask<bool> Handle(RefreshPortfolioCommand command, CancellationToken cancellationToken) => new(sync.Request());
}
