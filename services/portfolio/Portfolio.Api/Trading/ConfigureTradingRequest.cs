using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Api;

public sealed record ConfigureTradingRequest(TradingRuleSet Rules);
