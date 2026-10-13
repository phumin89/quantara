using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record ApproveOrderCommand(Guid Id, bool Confirmed) : ICommand<OrderView>;
