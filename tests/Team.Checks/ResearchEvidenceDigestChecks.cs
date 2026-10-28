using System.Text.Json;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchEvidenceDigestChecks
{
    public static void Run()
    {
        const string original = """[{"symbol":"BTC","quantity":2,"available":1}]""";
        const string missing = """[{"symbol":"BTC","quantity":2}]""";
        const string unknown = """[{"symbol":"BTC","quantity":2,"available":null}]""";
        const string zero = """[{"symbol":"BTC","quantity":2,"available":0}]""";
        (string Name, string Before, string After, bool Equivalent)[] cases =
        [
            ("quantity scale", original, """[{"symbol":"BTC","quantity":2.0000,"available":1}]""", true),
            ("available scale", original, """[{"symbol":"BTC","quantity":2,"available":1.0000}]""", true),
            ("exact exponents", original, """[{"symbol":"BTC","quantity":20e-1,"available":100e-2}]""", true),
            (
                "row ordering",
                """[{"symbol":"BTC","quantity":2,"available":1},{"symbol":"THB","quantity":2000.25,"available":1000.125}]""",
                """[{"symbol":"THB","quantity":2000.25,"available":1000.125},{"symbol":"BTC","quantity":2,"available":1}]""",
                true
            ),
            ("changed quantity", original, """[{"symbol":"BTC","quantity":3,"available":1}]""", false),
            ("changed available", original, """[{"symbol":"BTC","quantity":2,"available":1.1}]""", false),
            (
                "quantity changes by 1e-18",
                original,
                """[{"symbol":"BTC","quantity":2.000000000000000001,"available":1}]""",
                false
            ),
            (
                "available changes by 1e-18",
                original,
                """[{"symbol":"BTC","quantity":2,"available":1.000000000000000001}]""",
                false
            ),
            (
                "small exact exponent",
                """[{"symbol":"BTC","quantity":2,"available":0.000000000000000001}]""",
                """[{"symbol":"BTC","quantity":2,"available":1e-18}]""",
                true
            ),
            ("unknown versus zero", unknown, zero, false),
            ("missing versus explicit null", missing, unknown, false),
            ("missing versus zero", missing, zero, false),
            ("symbol case stays significant", original, """[{"symbol":"btc","quantity":2,"available":1}]""", false),
        ];
        foreach (var check in cases)
        {
            if ((Digest(check.Before) == Digest(check.After)) != check.Equivalent)
            {
                throw new InvalidOperationException("Research evidence digest mismatch: " + check.Name + ".");
            }
        }

        if (Digest(missing) != TeamResearchRouter.Digest("BTC:2:unknown"))
        {
            throw new InvalidOperationException("Missing available retains its existing unknown digest marker.");
        }

        // Legacy digest compatibility only; transport still requires a number or null.
        foreach (var token in new[] { "null", "\"unknown\"", "true", "{}" })
        {
            var balances = $$"""[{"symbol":"BTC","quantity":2,"available":{{token}}}]""";
            if (Digest(balances) != TeamResearchRouter.Digest("BTC:2:" + token))
            {
                throw new InvalidOperationException("Nonnumeric available retains its exact legacy fallback.");
            }
        }

        Console.WriteLine(
            "Research evidence digests: exact numeric equivalence, ordering, precise changes and unknown-value distinctions passed."
        );
    }

    private static string Digest(string balances)
    {
        using var document = JsonDocument.Parse("{\"walletEvidence\":{\"balances\":" + balances + "}}");
        return ResearchEvidence.HoldingsDigest(document.RootElement);
    }
}
