using Mediator;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class GetTradingEvaluationHandler(TradingEvaluationService service, IReadOnlyTradingStore store)
    : IQueryHandler<GetTradingEvaluationQuery, TradingEvaluationResult>
{
    public async ValueTask<TradingEvaluationResult> Handle(
        GetTradingEvaluationQuery query,
        CancellationToken cancellationToken
    )
    {
        var state = await store.Read(cancellationToken);
        return await service.Evaluate(
            new TradingEvaluationRequest(query.Symbols, Rules: state.Rules),
            cancellationToken
        );
    }
}
