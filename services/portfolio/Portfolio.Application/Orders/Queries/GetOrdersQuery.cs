using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record GetOrdersQuery : IQuery<IReadOnlyList<OrderView>>;
