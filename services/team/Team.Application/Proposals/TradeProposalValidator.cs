using System.Globalization;
using System.Text.Json;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public static class TradeProposalValidator
{
    public static void Validate(TradeProposal proposal, string context)
    {
        using var document = JsonDocument.Parse(context);
        var evidence = document.RootElement.GetProperty("evidence");
        var research = evidence.GetProperty("marketResearch");
        if (
            !research.GetProperty("available").GetBoolean()
            || research.GetProperty("fetchedAt").GetDateTimeOffset() != proposal.EvidenceAt
        )
        {
            throw new InvalidOperationException("Trade proposal does not reference verified market evidence.");
        }

        var market = research
            .GetProperty("markets")
            .EnumerateArray()
            .SingleOrDefault(item => item.GetProperty("symbol").GetString() == proposal.Symbol + "_THB");
        if (
            market.ValueKind == JsonValueKind.Undefined
            || !Enum.IsDefined(proposal.Side)
            || proposal.Reason.Length is < 1 or > 500
            || string.IsNullOrWhiteSpace(proposal.Reason)
        )
        {
            throw new InvalidOperationException("Trade proposal market, side or reason is invalid.");
        }

        var amount = TradeProposalValidator.Number(proposal.Amount);
        var price = TradeProposalValidator.Number(proposal.LimitPrice);
        var last = market.GetProperty("last").GetDecimal();
        if (
            last <= 0
            || decimal.Abs(price / last - 1) > 0.01m
            || market.GetProperty(proposal.Side == TradeProposalSide.Buy ? "ask" : "bid").GetDecimal() <= 0
        )
        {
            throw new InvalidOperationException("Trade proposal price is unsupported by current market evidence.");
        }

        var available =
            proposal.Side == TradeProposalSide.Buy
                ? evidence.GetProperty("summary").GetProperty("availableThb").GetDecimal()
                : evidence
                    .GetProperty("positions")
                    .EnumerateArray()
                    .Where(item => item.GetProperty("symbol").GetString() == proposal.Symbol)
                    .Select(item => item.GetProperty("quantity").GetDecimal())
                    .SingleOrDefault();
        if (amount > available)
        {
            throw new InvalidOperationException("Trade proposal exceeds the observed account balance.");
        }
    }

    private static decimal Number(string value)
    {
        if (
            value.Length > 40
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || number <= 0
            || number > 1000000000000m
        )
        {
            throw new InvalidOperationException("Trade proposal amount or price is invalid.");
        }

        return number;
    }
}
