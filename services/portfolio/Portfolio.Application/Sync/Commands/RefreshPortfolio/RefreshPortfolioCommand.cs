using Mediator;
namespace Quantara.Portfolio.Application;

public sealed record RefreshPortfolioCommand : ICommand<bool>;
