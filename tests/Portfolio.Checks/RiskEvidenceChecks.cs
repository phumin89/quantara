using Quantara.Portfolio.Infrastructure;
using Quantara.Portfolio.Infrastructure.Risk;

namespace Portfolio.Checks;

public static class RiskEvidenceChecks
{
    public static async Task Run()
    {
        await ReadsCompleteAccountEvidence();
        await RejectsChangingFinalBalances();
        await RejectsMissingWithdrawalFee();
        await RejectsInvalidPagination();
    }

    private static async Task ReadsCompleteAccountEvidence()
    {
        var handler = new RiskEvidenceHttpHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.bitkub.com") };
        var reader = new BitkubRiskEvidenceReader(
            new BitkubAccountReader(http, new BitkubCredentials("risk-key", "risk-secret")),
            TimeProvider.System
        );

        var evidence = await reader.Read(CancellationToken.None);
        Check(evidence.Balances.Length == 2, "risk evidence reads wallet balances twice coherently");
        Check(evidence.OpenOrders.Length == 2, "risk evidence keeps same broker order id per symbol");
        Check(
            evidence.OpenOrders.Select(order => order.Symbol).Distinct().Count() == 2,
            "risk evidence preserves order symbols"
        );
        Check(evidence.CashFlows.Length == 4, "risk evidence reads fiat and crypto transfers");
        Check(
            evidence.CashFlows.Single(flow => flow.Id.Contains("fiat-withdraw", StringComparison.Ordinal)).FeeQuantity
                == 10,
            "risk evidence preserves fiat withdrawal fee"
        );
        Check(
            evidence.CashFlows.Single(flow => flow.Id.Contains("deposit-hash", StringComparison.Ordinal)).ValueThb
                is null,
            "risk evidence keeps crypto transfer valuation unknown"
        );
        Check(
            handler.Calls.GetValueOrDefault("/api/v4/fiat/deposit/history") == 1,
            "risk evidence reads fiat deposits once"
        );
        Check(
            handler.Calls.GetValueOrDefault("/api/v4/fiat/withdraw/history") == 1,
            "risk evidence reads fiat withdrawals once"
        );
        Check(
            handler.Calls.GetValueOrDefault("/api/v4/crypto/deposits") == 1,
            "risk evidence reads crypto deposits once"
        );
        Check(
            handler.Calls.GetValueOrDefault("/api/v4/crypto/withdraws") == 1,
            "risk evidence reads crypto withdrawals once"
        );
    }

    private static async Task RejectsChangingFinalBalances()
    {
        await Rejects(
            RiskEvidenceFixtureMode.ChangingFinalBalance,
            "risk evidence rejects balances changing during collection"
        );
    }

    private static async Task RejectsMissingWithdrawalFee()
    {
        await Rejects(RiskEvidenceFixtureMode.MissingWithdrawalFee, "risk evidence rejects missing withdrawal fee");
    }

    private static async Task RejectsInvalidPagination()
    {
        await Rejects(RiskEvidenceFixtureMode.InvalidFiatPagination, "risk evidence rejects invalid fiat pagination");
    }

    private static async Task Rejects(RiskEvidenceFixtureMode mode, string name)
    {
        var handler = new RiskEvidenceHttpHandler(mode);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.bitkub.com") };
        var reader = new BitkubRiskEvidenceReader(
            new BitkubAccountReader(http, new BitkubCredentials("risk-key", "risk-secret")),
            TimeProvider.System
        );
        var rejected = false;
        try
        {
            await reader.Read(CancellationToken.None);
        }
        catch (Exception exception)
            when (exception is InvalidDataException or ExchangeReadException or KeyNotFoundException)
        {
            rejected = true;
        }

        Check(rejected, name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(name);
        }

        Console.WriteLine("Passed: " + name);
    }
}
