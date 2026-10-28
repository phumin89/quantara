namespace Quantara.Portfolio.Domain.Risk;

public static class CapitalFlowEvidence
{
    public static CapitalCashFlow[] Unique(IReadOnlyList<CapitalCashFlow> flows, DateTimeOffset observedAt)
    {
        var unique = new Dictionary<string, CapitalCashFlow>(StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            if (
                string.IsNullOrWhiteSpace(flow.Id)
                || flow.Id.Length > 256
                || string.IsNullOrWhiteSpace(flow.Symbol)
                || flow.Symbol.Length > 30
                || !flow.Symbol.All(char.IsAsciiLetterOrDigit)
                || !Enum.IsDefined(flow.Kind)
                || flow.Quantity <= 0
                || flow.FeeQuantity < 0
                || flow.FeeQuantity >= flow.Quantity
                || flow.ValueThb < 0
                || flow.At > observedAt
            )
            {
                throw new InvalidDataException("Invalid capital-flow evidence.");
            }

            if (!unique.TryAdd(flow.Id, flow) && unique[flow.Id] != flow)
            {
                throw new InvalidDataException("Conflicting capital-flow evidence for the same broker identity.");
            }
        }

        return unique.Values.OrderBy(flow => flow.At).ThenBy(flow => flow.Id, StringComparer.Ordinal).ToArray();
    }
}
