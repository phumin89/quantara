using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record RejectOrderCommand(Guid Id) : ICommand<OrderView>;
