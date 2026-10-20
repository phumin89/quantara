using System.Text.Json;
using System.Text.Json.Nodes;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class PaperCapitalProvenanceChecks
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var initial = TradingStateFactory.Create(now);
        var wallet = new PortfolioLedger { Balances = [new("THB", 777, 700)], UpdatedAt = now.AddSeconds(-10) };
        var paper = PaperCapitalSnapshot.Create("real-account", initial.Paper, [], initial.Rules, now);
        if (paper.Source != CapitalSource.Paper)
        {
            throw new InvalidOperationException("Simulation provenance must be explicit.");
        }

        var options = PortfolioJson.CreateOptions();
        var legacy =
            JsonSerializer.SerializeToNode(paper, options)?.AsObject()
            ?? throw new InvalidOperationException("Capital fixture missing.");
        legacy.Remove("Source");
        var restored =
            JsonSerializer.Deserialize<CapitalSnapshot>(legacy, options)
            ?? throw new InvalidOperationException("Legacy capital fixture missing.");
        if (restored.Source != CapitalSource.Unknown)
        {
            throw new InvalidOperationException("Legacy capital without provenance must remain Unknown.");
        }

        var status = new AutonomousTradingStatus(now, null, 0, 0, [], paper, "paper", now);
        foreach (var mode in new[] { TradingMode.Paper, TradingMode.Stopped, TradingMode.AutoLive })
        {
            var result = PortfolioSnapshotProjection.Current(
                wallet,
                initial with
                {
                    Mode = mode,
                    Autonomy = status,
                },
                "real-account",
                now
            );
            if (result.Balances.Single().Quantity != 777 || result.UpdatedAt != wallet.UpdatedAt)
            {
                throw new InvalidOperationException(
                    "Paper capital must never replace the real wallet, including after a mode switch."
                );
            }
        }

        Console.WriteLine("Paper capital provenance checks passed.");
    }
}
