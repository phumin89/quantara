using Mediator;

namespace Quantara.MarketData.Application;

public sealed class SetMarketWatchHandler(IMarketEventBus stream) : ICommandHandler<SetMarketWatchCommand, bool>
{
    public async ValueTask<bool> Handle(SetMarketWatchCommand command, CancellationToken cancellationToken)
    {
        await stream.SetWatch(command.Symbols, cancellationToken);
        return true;
    }
}
