using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class OrderDiscoveryChecks
{
    private const string incomplete = "Broker search was incomplete. Outcome remains unknown; no order was resent.";

    private const string ambiguous =
        "Several broker orders match this proposal. Outcome remains unknown; no order was resent.";

    private const string identityError = "Broker order identity did not match the proposal.";

    private const string lineageError = "Broker order lineage did not resolve to an original order.";

    public static async Task Run()
    {
        var clock = new OrderCheckTime();
        foreach (var side in new[] { TradeSide.Buy, TradeSide.Sell })
        {
            var proposal = new OrderProposal(
                Guid.NewGuid(),
                "BTC",
                side,
                100m,
                100m,
                100m,
                "Discovery fixture",
                clock.Now,
                clock.Now.AddMinutes(3),
                OrderState.Unknown,
                null,
                "Uncertain original submission"
            );
            foreach (var fixture in OrderDiscoveryChecks.Cases(proposal))
            {
                await OrderDiscoveryChecks.Expect(proposal, fixture, clock);
            }

            var known = new OrderDiscoveryCase(
                "known-ID-bypasses-discovery",
                "",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { ["root-a"] = OrderDiscoveryChecks.Info(proposal, "root-a", "root-a") },
                OrderState.Cancelled,
                "root-a",
                1,
                0
            );
            await OrderDiscoveryChecks.Expect(proposal with { BrokerOrderId = "root-a" }, known, clock);
        }

        Console.WriteLine(
            "Order discovery: unique canonical families, bounded complete scans and read-only unknown preservation passed with fake HTTP."
        );
    }

    private static OrderDiscoveryCase[] Cases(OrderProposal proposal)
    {
        var empty = new Dictionary<string, string> { [""] = OrderDiscoveryChecks.Page(proposal, []) };
        var roots = new Dictionary<string, string>
        {
            ["root-a"] = OrderDiscoveryChecks.Info(proposal, "root-a", "root-a"),
            ["root-b"] = OrderDiscoveryChecks.Info(proposal, "root-b", "root-b"),
        };
        var family = new Dictionary<string, string>(roots)
        {
            ["child-a"] = OrderDiscoveryChecks.Info(proposal, "child-a", "root-a", 25m),
            ["child-b"] = OrderDiscoveryChecks.Info(proposal, "child-b", "root-a", 75m),
            ["root-a"] = OrderDiscoveryChecks.Info(proposal, "root-a", "root-a", client: ""),
        };
        var manyChildren = Enumerable.Range(0, 8).Select(index => "child-" + index).ToArray();
        var manyOrders = manyChildren.ToDictionary(
            id => id,
            id => OrderDiscoveryChecks.Info(proposal, id, "root-a", 10m),
            StringComparer.Ordinal
        );
        manyOrders["root-a"] = roots["root-a"];
        return
        [
            new(
                "single-open",
                OrderDiscoveryChecks.Open(proposal, ["root-a"]),
                empty,
                roots,
                OrderState.Cancelled,
                "root-a",
                1,
                1
            ),
            new(
                "repeated-transactions-one-order",
                OrderDiscoveryChecks.Open(proposal, []),
                new Dictionary<string, string> { [""] = OrderDiscoveryChecks.Page(proposal, ["root-a", "root-a"]) },
                roots,
                OrderState.Cancelled,
                "root-a",
                1,
                1
            ),
            new(
                "open-and-history-same-order",
                OrderDiscoveryChecks.Open(proposal, ["root-a"]),
                new Dictionary<string, string> { [""] = OrderDiscoveryChecks.Page(proposal, ["root-a", "root-a"]) },
                roots,
                OrderState.Cancelled,
                "root-a",
                1,
                1
            ),
            new(
                "linked-family-cached-original",
                OrderDiscoveryChecks.Open(proposal, ["child-a", "child-b"]),
                new Dictionary<string, string>
                {
                    [""] = OrderDiscoveryChecks.Page(proposal, ["child-a", "root-a", "child-b"]),
                },
                family,
                OrderState.Cancelled,
                "root-a",
                3,
                1
            ),
            new(
                "independent-open-orders",
                OrderDiscoveryChecks.Open(proposal, ["root-a", "root-b"]),
                empty,
                roots,
                OrderState.Unknown,
                null,
                2,
                1,
                ambiguous
            ),
            new(
                "open-versus-history",
                OrderDiscoveryChecks.Open(proposal, ["root-a"]),
                new Dictionary<string, string> { [""] = OrderDiscoveryChecks.Page(proposal, ["root-b"]) },
                roots,
                OrderState.Unknown,
                null,
                2,
                1,
                ambiguous
            ),
            new(
                "second-page-other-family",
                OrderDiscoveryChecks.Open(proposal, []),
                new Dictionary<string, string>
                {
                    [""] = OrderDiscoveryChecks.Page(proposal, ["root-a"], true, "page-2"),
                    ["page-2"] = OrderDiscoveryChecks.Page(proposal, ["root-b"]),
                },
                roots,
                OrderState.Unknown,
                null,
                2,
                2,
                ambiguous
            ),
            new(
                "complete-third-page-one-family",
                OrderDiscoveryChecks.Open(proposal, []),
                new Dictionary<string, string>
                {
                    [""] = OrderDiscoveryChecks.Page(proposal, ["root-a"], true, "page-2"),
                    ["page-2"] = OrderDiscoveryChecks.Page(proposal, ["root-a"], true, "page-3"),
                    ["page-3"] = OrderDiscoveryChecks.Page(proposal, ["root-a"]),
                },
                roots,
                OrderState.Cancelled,
                "root-a",
                1,
                3
            ),
            new(
                "third-page-still-incomplete",
                OrderDiscoveryChecks.Open(proposal, ["root-a"]),
                new Dictionary<string, string>
                {
                    [""] = OrderDiscoveryChecks.Page(proposal, [], true, "page-2"),
                    ["page-2"] = OrderDiscoveryChecks.Page(proposal, [], true, "page-3"),
                    ["page-3"] = OrderDiscoveryChecks.Page(proposal, [], true, "page-4"),
                },
                roots,
                OrderState.Unknown,
                null,
                0,
                3,
                incomplete
            ),
            new(
                "repeated-cursor",
                OrderDiscoveryChecks.Open(proposal, ["root-a"]),
                new Dictionary<string, string>
                {
                    [""] = OrderDiscoveryChecks.Page(proposal, [], true, "page-2"),
                    ["page-2"] = OrderDiscoveryChecks.Page(proposal, [], true, "page-2"),
                },
                roots,
                OrderState.Unknown,
                null,
                0,
                2,
                incomplete
            ),
            new(
                "missing-cursor",
                OrderDiscoveryChecks.Open(proposal, ["root-a"]),
                new Dictionary<string, string> { [""] = OrderDiscoveryChecks.Page(proposal, [], true) },
                roots,
                OrderState.Unknown,
                null,
                0,
                1,
                incomplete
            ),
            new(
                "missing-pagination",
                OrderDiscoveryChecks.Open(proposal, ["root-a"]),
                new Dictionary<string, string> { [""] = "{\"error\":0,\"result\":[]}" },
                roots,
                OrderState.Unknown,
                null,
                0,
                1,
                incomplete
            ),
            new(
                "candidate-budget",
                OrderDiscoveryChecks.Open(proposal, Enumerable.Range(0, 9).Select(index => "order-" + index).ToArray()),
                empty,
                roots,
                OrderState.Unknown,
                null,
                0,
                0,
                incomplete
            ),
            new(
                "actual-resolution-budget",
                OrderDiscoveryChecks.Open(proposal, manyChildren),
                empty,
                manyOrders,
                OrderState.Unknown,
                null,
                8,
                1,
                incomplete
            ),
            new(
                "wrong-child-client",
                OrderDiscoveryChecks.Open(proposal, ["child-a"]),
                empty,
                new Dictionary<string, string>(roots)
                {
                    ["child-a"] = OrderDiscoveryChecks.Info(proposal, "child-a", "root-a", 25m, "different-client"),
                },
                OrderState.Unknown,
                null,
                1,
                1,
                ExpectedError: identityError
            ),
            new(
                "wrong-root-client",
                OrderDiscoveryChecks.Open(proposal, ["child-a"]),
                empty,
                new Dictionary<string, string>(family)
                {
                    ["root-a"] = OrderDiscoveryChecks.Info(proposal, "root-a", "root-a", client: "different-client"),
                },
                OrderState.Unknown,
                null,
                2,
                1,
                ExpectedError: identityError
            ),
            new(
                "redirect-cycle",
                OrderDiscoveryChecks.Open(proposal, ["child-a"]),
                empty,
                new Dictionary<string, string>(family)
                {
                    ["root-a"] = OrderDiscoveryChecks.Info(proposal, "root-a", "child-a"),
                },
                OrderState.Unknown,
                null,
                2,
                1,
                ExpectedError: lineageError
            ),
            new(
                "unresolved-redirect-chain",
                OrderDiscoveryChecks.Open(proposal, ["child-a"]),
                empty,
                new Dictionary<string, string>(family)
                {
                    ["child-a"] = OrderDiscoveryChecks.Info(proposal, "child-a", "child-b", 25m),
                },
                OrderState.Unknown,
                null,
                2,
                1,
                ExpectedError: lineageError
            ),
            new(
                "valid-root-cannot-hide-invalid-candidate",
                OrderDiscoveryChecks.Open(proposal, ["root-a", "z-child"]),
                empty,
                new Dictionary<string, string>(roots)
                {
                    ["z-child"] = OrderDiscoveryChecks.Info(proposal, "z-child", "root-a", 25m, "different-client"),
                },
                OrderState.Unknown,
                null,
                2,
                1,
                ExpectedError: identityError
            ),
            new(
                "no-match",
                OrderDiscoveryChecks.Open(proposal, []),
                empty,
                roots,
                OrderState.Unknown,
                null,
                0,
                1,
                "No exact broker match found. Outcome remains unknown; no order was resent."
            ),
        ];
    }

    private static async Task Expect(OrderProposal proposal, OrderDiscoveryCase fixture, OrderCheckTime clock)
    {
        using var handler = new TradingBrokerHttpHandler
        {
            Response = path => OrderDiscoveryChecks.Response(path, fixture),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://bitkub.invalid/") };
        var credentials = new BitkubCredentials("fake-key", "fake-secret");
        var broker = new BitkubOrderBroker(http, credentials, new BitkubAccountReader(http, credentials));
        var store = new FakeOrderStore();
        await store.Create(proposal, CancellationToken.None);
        try
        {
            await new ReconcileOrderHandler(store, broker, clock).Handle(new(proposal.Id), CancellationToken.None);
            OrderDiscoveryChecks.Check(fixture.ExpectedError is null, fixture.Name + ": missing expected rejection");
        }
        catch (InvalidDataException error) when (error.Message == fixture.ExpectedError) { }

        var current =
            await store.Find(proposal.Id, CancellationToken.None)
            ?? throw new InvalidOperationException("Missing discovery proposal.");
        if (fixture.ExpectedError is not null)
        {
            OrderDiscoveryChecks.Check(
                current == proposal,
                fixture.Name + ": rejected evidence altered the unknown proposal"
            );
        }
        else
        {
            OrderDiscoveryChecks.Check(
                current.State == fixture.ExpectedState && current.BrokerOrderId == fixture.ExpectedOrderId,
                fixture.Name + ": incorrect reconciliation result"
            );
            OrderDiscoveryChecks.Check(
                current with
                {
                    State = proposal.State,
                    BrokerOrderId = proposal.BrokerOrderId,
                    Message = proposal.Message,
                } == proposal,
                fixture.Name + ": immutable proposal fields changed"
            );
            if (fixture.ExpectedMessage is not null)
            {
                OrderDiscoveryChecks.Check(
                    current.Message == fixture.ExpectedMessage,
                    fixture.Name + ": lost uncertainty diagnostics"
                );
            }
        }

        OrderDiscoveryChecks.Check(
            handler.Posts == 0 && handler.GetPaths.Count <= 24,
            fixture.Name + ": broker mutation or unbounded GETs"
        );
        OrderDiscoveryChecks.Check(
            handler.GetPaths.Count(path => path.StartsWith("/api/v3/market/order-info?", StringComparison.Ordinal))
                == fixture.ExpectedInfoReads,
            fixture.Name + ": order-info reads were not bounded/cached"
        );
        OrderDiscoveryChecks.Check(
            handler.GetPaths.Count(path =>
                path.StartsWith("/api/v3/market/my-order-history?", StringComparison.Ordinal)
            ) == fixture.ExpectedHistoryReads,
            fixture.Name + ": history stopped early or exceeded its budget"
        );
    }

    private static string Response(string path, OrderDiscoveryCase fixture)
    {
        var uri = new Uri("https://bitkub.invalid" + path);
        var query = uri
            .Query.TrimStart('?')
            .Split('&')
            .Select(value => value.Split('=', 2))
            .ToDictionary(
                parts => parts[0],
                parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "",
                StringComparer.Ordinal
            );
        return uri.AbsolutePath switch
        {
            "/api/v3/market/my-open-orders" => fixture.OpenOrders,
            "/api/v3/market/my-order-history" => fixture.History[query.GetValueOrDefault("cursor", "")],
            "/api/v3/market/order-info" => fixture.OrderInfo[query["id"]],
            _ => throw new InvalidOperationException("Unexpected fixture endpoint."),
        };
    }

    private static string Open(OrderProposal proposal, string[] ids) =>
        JsonSerializer.Serialize(
            new
            {
                error = 0,
                result = ids.Select(id => new
                {
                    id,
                    client_id = proposal.Id.ToString("N"),
                    side = proposal.Side == TradeSide.Buy ? "buy" : "sell",
                }),
            }
        );

    private static string Page(OrderProposal proposal, string[] ids, bool hasNext = false, string? cursor = null) =>
        JsonSerializer.Serialize(
            new
            {
                error = 0,
                result = ids.Select(
                    (id, index) =>
                        new
                        {
                            order_id = id,
                            txn_id = "execution-" + index,
                            client_id = proposal.Id.ToString("N"),
                            side = proposal.Side == TradeSide.Buy ? "buy" : "sell",
                        }
                ),
                pagination = new { has_next = hasNext, cursor },
            }
        );

    private static string Info(
        OrderProposal proposal,
        string id,
        string first,
        decimal amount = 100m,
        string? client = null
    ) =>
        JsonSerializer.Serialize(
            new
            {
                error = 0,
                result = new
                {
                    id,
                    first,
                    client_id = client ?? proposal.Id.ToString("N"),
                    amount,
                    rate = proposal.LimitPrice,
                    remaining = amount,
                    filled = 0m,
                    status = "cancelled",
                    partial_filled = false,
                    history = Array.Empty<object>(),
                },
            }
        );

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
