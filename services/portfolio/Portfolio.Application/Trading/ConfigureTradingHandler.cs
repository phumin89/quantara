using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class ConfigureTradingHandler(ITradingControl control, TradingOverviewProjection projection)
    : ICommandHandler<ConfigureTradingCommand, string>
{
    public async ValueTask<string> Handle(ConfigureTradingCommand command, CancellationToken cancellationToken) =>
        projection.Serialize(await control.Configure(command.Rules, cancellationToken));
}
