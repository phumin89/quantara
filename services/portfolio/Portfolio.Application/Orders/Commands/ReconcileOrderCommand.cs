using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record ReconcileOrderCommand(Guid Id) : ICommand<OrderView>;
