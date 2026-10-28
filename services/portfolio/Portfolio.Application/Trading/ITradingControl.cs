using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingControl
{
    public Task<TradingState> Configure(TradingRuleSet rules, CancellationToken cancellationToken);

    public Task<TradingState> PrepareAdaptive(
        string accountKey,
        string expectedRulesHash,
        CancellationToken cancellationToken
    );

    public Task<TradingState> SetMode(TradingMode mode, CancellationToken cancellationToken);

    public Task<TradingState> Activate(
        string accountKey,
        string rulesHash,
        DateTimeOffset expiresAt,
        string acknowledgement,
        CancellationToken cancellationToken,
        bool aiManaged = false
    );

    public Task<TradingState> Disable(bool emergency, CancellationToken cancellationToken);
}
