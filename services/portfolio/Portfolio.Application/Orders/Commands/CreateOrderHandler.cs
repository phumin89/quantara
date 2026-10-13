using System.Globalization;
using Mediator;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed class CreateOrderHandler(IOrderStore store, IOrderBroker broker, TimeProvider time)
    : ICommandHandler<CreateOrderCommand, OrderView>
{
    public async ValueTask<OrderView> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        if (
            string.IsNullOrWhiteSpace(command.Symbol)
            || command.Symbol.Length > 30
            || !command.Symbol.All(char.IsAsciiLetterOrDigit)
            || !Enum.IsDefined(command.Side)
            || string.IsNullOrWhiteSpace(command.Reason)
            || command.Reason.Length > 500
        )
        {
            throw new OrderValidationException("Enter a valid symbol, side and reason (1–500 characters).");
        }

        var amount = CreateOrderHandler.Number(command.Amount);
        var price = CreateOrderHandler.Number(command.LimitPrice);
        var symbol = command.Symbol.ToUpperInvariant();
        var market = await broker.Market(symbol, command.Side, cancellationToken);
        var now = time.GetUtcNow();
        var proposal = new OrderProposal(
            Guid.NewGuid(),
            symbol,
            command.Side,
            amount,
            price,
            market.ReferencePrice,
            command.Reason.Trim(),
            now,
            now.AddMinutes(3),
            OrderState.Pending,
            null,
            null
        );
        OrderRules.Validate(proposal, market);
        await store.Create(proposal, cancellationToken);
        return OrderView.From(proposal, now);
    }

    private static decimal Number(string text)
    {
        if (
            text is null
            || text.Length > 40
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || number <= 0
            || number > 1000000000000m
        )
        {
            throw new OrderValidationException("Amount and limit price must be positive decimal numbers.");
        }

        return number;
    }
}
