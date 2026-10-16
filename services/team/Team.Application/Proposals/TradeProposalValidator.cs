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
        var accountKey = evidence.GetProperty("accountKey").GetString();
        if (string.IsNullOrWhiteSpace(accountKey) || accountKey.Length != 64 || !accountKey.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Trade proposal account evidence is invalid.");
        }

        var wallet = evidence.GetProperty("walletEvidence");
        var sizingProfile = wallet.TryGetProperty("sizingProfile", out var profileValue)
            ? profileValue.GetString()
            : "disabled";
        if (sizingProfile is not ("aggressive" or "disabled"))
        {
            throw new InvalidOperationException("Trade proposal sizing profile is invalid.");
        }

        if (sizingProfile == "aggressive")
        {
            if (
                !wallet.TryGetProperty("riskPolicyHash", out var hashValue)
                || hashValue.GetString() is not { Length: 64 } policyHash
                || !policyHash.All(Uri.IsHexDigit)
                || proposal.Side == TradeProposalSide.Buy && proposal.EntryPlan is null
            )
            {
                throw new InvalidOperationException(
                    "Aggressive proposals require a verified risk policy and a buy entry plan."
                );
            }
        }

        if (wallet.GetProperty("accountKey").GetString() != accountKey)
        {
            throw new InvalidOperationException("Trade proposal wallet evidence is bound to another account.");
        }

        var walletAge = DateTimeOffset.UtcNow - wallet.GetProperty("observedAt").GetDateTimeOffset();
        if (walletAge > TimeSpan.FromMinutes(10) || walletAge < TimeSpan.FromSeconds(-5))
        {
            throw new InvalidOperationException("Trade proposal wallet evidence is stale or from the future.");
        }

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
        var restingBuy = proposal.Side == TradeProposalSide.Buy && price < last && proposal.EntryPlan is not null;
        if (
            last <= 0
            || (!restingBuy && decimal.Abs(price / last - 1) > 0.01m)
            || market.GetProperty(proposal.Side == TradeProposalSide.Buy ? "ask" : "bid").GetDecimal() <= 0
        )
        {
            throw new InvalidOperationException("Trade proposal price is unsupported by current market evidence.");
        }

        if (proposal.EntryPlan is { } plan)
        {
            var target = TradeProposalValidator.Number(plan.TargetPrice);
            var invalidation = TradeProposalValidator.Number(plan.InvalidationPrice);
            if (
                proposal.Side != TradeProposalSide.Buy
                || target <= price
                || invalidation >= price
                || plan.ReviewAfterMinutes is < 1 or > 1440
            )
            {
                throw new InvalidOperationException("Entry plan target, invalidation or review horizon is invalid.");
            }

            _ = TradeProposalDescription.Format(proposal);
        }

        var availableSymbol = proposal.Side == TradeProposalSide.Buy ? "THB" : proposal.Symbol;
        var availableBalance = wallet
            .GetProperty("balances")
            .EnumerateArray()
            .SingleOrDefault(item => item.GetProperty("symbol").GetString() == availableSymbol);
        var available =
            availableBalance.ValueKind == JsonValueKind.Undefined
            || availableBalance.GetProperty("available").ValueKind == JsonValueKind.Null
                ? (decimal?)null
                : availableBalance.GetProperty("available").GetDecimal();
        if (available is null || available <= 0 || (sizingProfile == "disabled" && amount > available))
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
