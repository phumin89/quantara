using System.Globalization;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Infrastructure.Risk;

public sealed class BitkubRiskEvidenceReader(BitkubAccountReader accountReader, TimeProvider time) : IRiskEvidenceReader
{
    private const int maximumSymbols = 1000;

    private const int maximumPages = 1000;

    public async Task<RiskAccountEvidence> Read(CancellationToken cancellationToken)
    {
        var observedAt = time.GetUtcNow();
        var balances = await accountReader.Balances(cancellationToken);
        var symbols = await this.ReadSymbols(cancellationToken);
        var openOrders = await this.ReadOpenOrders(symbols, cancellationToken);
        var cashFlows = await this.ReadCashFlows(cancellationToken);
        var finalBalances = await accountReader.Balances(cancellationToken);
        if (
            !balances
                .OrderBy(balance => balance.Symbol, StringComparer.Ordinal)
                .SequenceEqual(finalBalances.OrderBy(balance => balance.Symbol, StringComparer.Ordinal))
        )
        {
            throw new InvalidDataException(
                "Balances changed while collecting account evidence. Refresh a coherent snapshot."
            );
        }

        return new(observedAt, balances, openOrders, cashFlows, observedAt.AddDays(-90));
    }

    private async Task<string[]> ReadSymbols(CancellationToken cancellationToken)
    {
        using var json = await accountReader.Get("/api/v3/market/symbols", false, cancellationToken);
        var rawSymbols = json
            .RootElement.GetProperty("result")
            .EnumerateArray()
            .Where(item =>
                Text(item, "market_segment") == "SPOT"
                && Text(item, "source") == "exchange"
                && Text(item, "status") == "active"
            )
            .Select(item => Text(item, "symbol"))
            .Where(symbol => symbol.EndsWith("_THB", StringComparison.OrdinalIgnoreCase))
            .Select(symbol => symbol[..^4].ToUpperInvariant())
            .ToArray();
        if (
            rawSymbols.Length is 0 or > maximumSymbols
            || rawSymbols.Distinct(StringComparer.Ordinal).Count() != rawSymbols.Length
            || rawSymbols.Any(symbol => symbol.Length is < 1 or > 30 || !symbol.All(char.IsAsciiLetterOrDigit))
        )
        {
            throw new ExchangeReadException("Bitkub symbol universe exceeded the safety limit.");
        }

        return rawSymbols;
    }

    private async Task<RiskOpenOrder[]> ReadOpenOrders(string[] symbols, CancellationToken cancellationToken)
    {
        var orders = new List<RiskOpenOrder>();
        var identities = new Dictionary<string, RiskOpenOrder>(StringComparer.Ordinal);
        foreach (var batch in symbols.Chunk(8))
        {
            var rows = await Task.WhenAll(batch.Select(symbol => this.ReadSymbolOrders(symbol, cancellationToken)));
            foreach (var order in rows.SelectMany(items => items))
            {
                var identity = order.Symbol + ":" + order.Side + ":" + order.Id;
                if (!identities.TryAdd(identity, order))
                {
                    if (identities[identity] != order)
                    {
                        throw new InvalidDataException("Bitkub returned conflicting open-order identities.");
                    }

                    continue;
                }

                orders.Add(order);
            }
        }

        return orders.ToArray();
    }

    private async Task<RiskOpenOrder[]> ReadSymbolOrders(string symbol, CancellationToken cancellationToken)
    {
        var path = "/api/v3/market/my-open-orders?sym=" + Uri.EscapeDataString(symbol.ToLowerInvariant() + "_thb");
        using var json = await accountReader.Get(path, true, cancellationToken);
        var rows = json.RootElement.GetProperty("result");
        if (rows.GetArrayLength() > 100000)
        {
            throw new InvalidDataException("Open-order evidence exceeded the safety budget.");
        }

        return rows.EnumerateArray().Select(item => ParseOrder(item, symbol)).ToArray();
    }

    private async Task<CapitalCashFlow[]> ReadCashFlows(CancellationToken cancellationToken)
    {
        var flows = new Dictionary<string, CapitalCashFlow>(StringComparer.Ordinal);
        await this.ReadFiatFlows("/api/v4/fiat/deposit/history", CashFlowKind.Deposit, flows, cancellationToken);
        await this.ReadFiatFlows("/api/v4/fiat/withdraw/history", CashFlowKind.Withdrawal, flows, cancellationToken);
        await this.ReadCryptoFlows("/api/v4/crypto/deposits", CashFlowKind.Deposit, flows, cancellationToken);
        await this.ReadCryptoFlows("/api/v4/crypto/withdraws", CashFlowKind.Withdrawal, flows, cancellationToken);

        return flows.Values.ToArray();
    }

    private async Task ReadFiatFlows(
        string endpoint,
        CashFlowKind kind,
        Dictionary<string, CapitalCashFlow> flows,
        CancellationToken cancellationToken
    )
    {
        for (var page = 1; page <= maximumPages; page++)
        {
            using var json = await accountReader.Get($"{endpoint}?page={page}&limit=100", true, cancellationToken);
            var rows = json.RootElement.GetProperty("data").EnumerateArray().ToArray();
            if (rows.Length > 100 || json.RootElement.GetProperty("pagination").GetProperty("page").GetInt32() != page)
            {
                throw new InvalidDataException("Invalid fiat cash-flow pagination.");
            }

            foreach (var item in rows)
            {
                if (Text(item, "status") != "complete")
                {
                    continue;
                }

                var id = endpoint + ":" + Text(item, "txn_id");
                if (Text(item, "currency") != "THB")
                {
                    throw new InvalidDataException("Unexpected fiat currency.");
                }

                var flow = ParseFlow(item, id, Text(item, "currency"), kind, true, time.GetUtcNow());
                AddFlow(flows, flow);
            }

            if (rows.Length < 100)
            {
                return;
            }
        }

        throw new ExchangeReadException("Bitkub fiat history exceeded the safety page limit.");
    }

    private async Task ReadCryptoFlows(
        string endpoint,
        CashFlowKind kind,
        Dictionary<string, CapitalCashFlow> flows,
        CancellationToken cancellationToken
    )
    {
        for (var page = 1; page <= maximumPages; page++)
        {
            using var json = await accountReader.Get(
                $"{endpoint}?status=complete&page={page}&limit=200",
                true,
                cancellationToken
            );
            var data = json.RootElement.GetProperty("data");
            if (data.GetProperty("page").GetInt32() != page)
            {
                throw new InvalidDataException("Bitkub crypto pagination did not advance.");
            }

            var rows = data.GetProperty("items").EnumerateArray().ToArray();
            foreach (var item in rows)
            {
                if (Text(item, "status") != "complete")
                {
                    throw new InvalidDataException("Unexpected incomplete crypto flow.");
                }

                var itemSymbol = Text(item, "symbol").ToUpperInvariant();

                var idName = kind == CashFlowKind.Deposit ? "hash" : "txn_id";
                var id = endpoint + ":" + Text(item, idName);
                var flow = ParseFlow(item, id, itemSymbol, kind, false, time.GetUtcNow());
                AddFlow(flows, flow);
            }

            var totalPage = data.GetProperty("total_page").GetInt32();
            if (
                totalPage < 0
                || totalPage > maximumPages
                || rows.Length > 200
                || totalPage == 0 && rows.Length > 0
                || totalPage > 0 && totalPage < page
            )
            {
                throw new InvalidDataException("Invalid crypto cash-flow pagination.");
            }

            if (page >= totalPage)
            {
                return;
            }
        }

        throw new ExchangeReadException("Bitkub crypto history exceeded the safety page limit.");
    }

    private static CapitalCashFlow ParseFlow(
        JsonElement item,
        string id,
        string symbol,
        CashFlowKind kind,
        bool thb,
        DateTimeOffset observedAt
    )
    {
        var amount = Number(item, "amount");
        var fee = kind == CashFlowKind.Withdrawal ? Number(item, "fee") : 0;
        var seconds = item.TryGetProperty("time", out var timeValue)
            ? timeValue.GetInt64()
            : DateTimeOffset.Parse(Text(item, "completed_at"), CultureInfo.InvariantCulture).ToUnixTimeSeconds();
        var at = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (
            string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(symbol)
            || amount <= 0
            || fee < 0
            || at > observedAt
        )
        {
            throw new InvalidDataException("Invalid Bitkub capital flow.");
        }

        if (symbol.Length > 30 || !symbol.All(char.IsAsciiLetterOrDigit) || thb && (symbol != "THB" || fee >= amount))
        {
            throw new InvalidDataException("Invalid external flow currency or fee.");
        }

        return new(id, symbol, amount, fee, at, kind, thb ? amount - fee : null);
    }

    private static void AddFlow(Dictionary<string, CapitalCashFlow> flows, CapitalCashFlow flow)
    {
        if (!flows.TryAdd(flow.Id, flow) && flows[flow.Id] != flow)
        {
            throw new InvalidDataException("Bitkub returned conflicting capital-flow identities.");
        }
    }

    private static RiskOpenOrder ParseOrder(JsonElement item, string symbol)
    {
        var id = Text(item, "id");
        var side = Text(item, "side") switch
        {
            "buy" => TradeSide.Buy,
            "sell" => TradeSide.Sell,
            _ => throw new InvalidDataException("Invalid Bitkub order side."),
        };
        var remaining = Number(item, "amount");
        var price = Number(item, "rate");
        if (string.IsNullOrWhiteSpace(id) || remaining <= 0 || price <= 0)
        {
            throw new InvalidDataException("Invalid Bitkub open order.");
        }

        return new(id, symbol, side, remaining, price);
    }

    private static string Text(JsonElement item, string name)
    {
        var text = item.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(text) ? text : throw new InvalidDataException("Missing Bitkub field.");
    }

    private static decimal Number(JsonElement item, string name) => Number(item.GetProperty(name));

    private static decimal Number(JsonElement value) =>
        decimal.Parse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
