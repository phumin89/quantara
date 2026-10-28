using System.Reflection;
using System.Text.Json;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class OrderStorageIdentityChecks
{
    public static void Run()
    {
        var rowId = new Guid("00000001-0000-0000-0000-000000000000");
        var now = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        var proposal = new OrderProposal(
            rowId,
            "BTC",
            TradeSide.Buy,
            100m,
            100m,
            100m,
            "Stored order identity fixture",
            now,
            now.AddMinutes(5),
            OrderState.Pending,
            null,
            null
        );
        var read =
            typeof(SqlOrderStore).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Stored order reader is missing.");
        var options = PortfolioJson.CreateOptions();
        var payload = JsonSerializer.Serialize(proposal, options);
        var retained = read.Invoke(null, [rowId, payload, nameof(OrderState.Submitting)]) as OrderProposal;
        if (retained != (proposal with { State = OrderState.Submitting }))
        {
            throw new InvalidOperationException(
                "A matching row retains its exact proposal and authoritative claim state."
            );
        }

        foreach (var otherId in new[] { Guid.Empty, new Guid("00000002-0000-0000-0000-000000000000") })
        {
            var conflicting = JsonSerializer.Serialize(proposal with { Id = otherId }, options);
            try
            {
                read.Invoke(null, [rowId, conflicting, nameof(OrderState.Pending)]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidDataException)
            {
                continue;
            }

            throw new InvalidOperationException(
                "A stored payload cannot redirect its SQL row to another order identity."
            );
        }
    }
}
