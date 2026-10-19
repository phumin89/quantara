namespace Quantara.Portfolio.Application;

public interface IPortfolioSync : IReadOnlyPortfolioSync
{
    public bool Request();
}
