using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class GetAccountHandler(IAccountContext account) : IQueryHandler<GetAccountQuery, string>
{
    public ValueTask<string> Handle(GetAccountQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(account.AccountKey);
}
