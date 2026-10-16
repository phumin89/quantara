using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class SetTradingModeHandler(ITradingControl control, TradingOverviewProjection projection)
    : ICommandHandler<SetTradingModeCommand, string>
{
    public async ValueTask<string> Handle(SetTradingModeCommand command, CancellationToken cancellationToken) =>
        projection.Serialize(await control.SetMode(command.Mode, cancellationToken));
}
