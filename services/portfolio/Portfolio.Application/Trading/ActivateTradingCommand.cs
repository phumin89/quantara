using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record ActivateTradingCommand(
    string AccountKey,
    string RulesHash,
    DateTimeOffset ExpiresAt,
    string Acknowledgement,
    bool AiManaged = false
) : ICommand<string>;
