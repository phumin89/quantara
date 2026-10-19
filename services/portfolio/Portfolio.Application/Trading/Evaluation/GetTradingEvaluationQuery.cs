using Mediator;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed record GetTradingEvaluationQuery(IReadOnlyList<string> Symbols) : IQuery<TradingEvaluationResult>;
