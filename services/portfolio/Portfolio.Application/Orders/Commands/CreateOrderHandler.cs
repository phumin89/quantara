using System.Globalization;
using Mediator;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed class CreateOrderHandler(
    IOrderStore store,
    IOrderBroker broker,
    TimeProvider time,
    IAccountContext? account = null
) : ICommandHandler<CreateOrderCommand, OrderView>
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
        if (command.SourceRunId is not null || command.ExpectedAccountKey is not null)
        {
            if (
                command.SourceRunId is null
                || command.SourceRunId == Guid.Empty
                || account is null
                || string.IsNullOrWhiteSpace(command.ExpectedAccountKey)
                || command.ExpectedAccountKey != account?.AccountKey
            )
            {
                throw new OrderValidationException(
                    "Proposal account changed or source task is missing. Run fresh research."
                );
            }

            var existing = await store.Find(command.SourceRunId.Value, cancellationToken);
            if (existing is not null)
            {
                if (
                    existing.Symbol != symbol
                    || existing.Side != command.Side
                    || existing.Amount != amount
                    || existing.LimitPrice != price
                    || existing.Reason != command.Reason.Trim()
                )
                {
                    throw new OrderValidationException("Source task already has a different immutable proposal.");
                }

                return OrderView.From(existing, time.GetUtcNow());
            }
        }

        var market = await broker.Market(symbol, command.Side, cancellationToken);
        var now = time.GetUtcNow();
        var proposal = new OrderProposal(
            command.SourceRunId ?? Guid.NewGuid(),
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
