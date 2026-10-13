using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class AccountContext(string accountKey) : IAccountContext
{
    public string AccountKey { get; } = accountKey;
}
