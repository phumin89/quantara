using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Quantara.Portfolio.Api;
using Quantara.Portfolio.Application;

var context = new DefaultHttpContext();
var request = context.Request;
var options = new TradingControlOptions("fake-private-control-token");
request.Host = new HostString("localhost", 5050);
request.Method = "GET";
Check(TradingRequestPolicy.Allows(request, "portfolio", options), "local read-only status");
request.Method = "POST";
request.Path = "/api/trading/prepare";
request.ContentType = "application/json";
Check(!TradingRequestPolicy.Allows(request, "portfolio", options), "missing token denied");
request.Headers["X-Quantara-Trading-Control"] = "wrong-token";
Check(!TradingRequestPolicy.Allows(request, "portfolio", options), "wrong token denied");
request.Headers["X-Quantara-Trading-Control"] = options.OwnerToken;
Check(TradingRequestPolicy.Allows(request, "portfolio", options), "server capability grants local controls only");
request.Headers.Origin = "https://external.invalid";
Check(!TradingRequestPolicy.Allows(request, "portfolio", options), "external origin denied");
request.Headers.Origin = "http://127.0.0.1:3000";
request.Headers["Sec-Fetch-Site"] = "cross-site";
Check(!TradingRequestPolicy.Allows(request, "portfolio", options), "cross-site denied despite token");
request.Headers.Remove("Sec-Fetch-Site");
request.Host = new HostString("attacker.invalid");
Check(!TradingRequestPolicy.Allows(request, "portfolio", options), "foreign host denied");
request.Host = new HostString("portfolio", 5050);
Check(TradingRequestPolicy.Allows(request, "portfolio", options), "configured internal host allowed");
Check(
    !TradingRequestPolicy.Allows(request, "portfolio", new TradingControlOptions("")),
    "unconfigured controls fail closed"
);
request.ContentType = "text/plain";
Check(!TradingRequestPolicy.Allows(request, "portfolio", options), "non-JSON controls denied");
request.ContentType = "application/json";
request.Headers["X-Quantara-Trading-Control"] = new Microsoft.Extensions.Primitives.StringValues([
    options.OwnerToken,
    options.OwnerToken,
]);
Check(!TradingRequestPolicy.Allows(request, "portfolio", options), "multiple capability headers denied");
request.Headers["X-Quantara-Trading-Control"] = options.OwnerToken;
request.ContentType = "application/x-www-form-urlencoded";
request.Path = "/api/trading/jobs/stats";
Check(TradingRequestPolicy.Allows(request, "portfolio", options), "authenticated read-only dashboard statistics form");
request.Path = "/api/trading/jobs/recurring/trigger";
Check(!TradingRequestPolicy.Allows(request, "portfolio", options), "dashboard commands never accept form controls");

foreach (var size in new[] { 0, 8192, 8193, 10000 })
{
    var bodyContext = new DefaultHttpContext();
    await using var body = new MemoryStream(Encoding.UTF8.GetBytes(new string(' ', size)));
    bodyContext.Request.Body = body;
    Check(
        await TradingRequestPolicy.BodyWithinLimit(bodyContext.Request, CancellationToken.None) == (size <= 8192),
        "streamed control body bound " + size
    );
    Check(bodyContext.Request.Body.Position == 0, "body restored for MVC validation");
}

foreach (
    var path in new[]
    {
        "/api/trading/reconciliation/manual-sale/preview",
        "/api/trading/reconciliation/manual-sale/apply",
        "/api/trading/reconciliation/manual-sale/cash-baseline/preview",
        "/api/trading/reconciliation/manual-sale/cash-baseline/apply",
    }
)
{
    request.Path = path;
    request.ContentType = "application/json";
    request.Headers.Remove("X-Quantara-Trading-Control");
    Check(
        !TradingRequestPolicy.Allows(request, "portfolio", options),
        "reconciliation review requires existing owner controls"
    );
    request.Headers["X-Quantara-Trading-Control"] = options.OwnerToken;
    Check(
        TradingRequestPolicy.Allows(request, "portfolio", options),
        "owner review paths retain existing local authorization policy"
    );
}

foreach (
    var (method, readOnly) in new[]
    {
        ("GET", true),
        ("get", true),
        ("HEAD", true),
        ("POST", false),
        ("PUT", false),
        ("PATCH", false),
        ("DELETE", false),
    }
)
{
    var failureContext = new DefaultHttpContext();
    failureContext.Request.Method = method;
    failureContext.Request.Path = "/api/crypto/orders/synthetic/approve";
    failureContext.Request.Headers["X-Synthetic-Secret"] = "do-not-leak";
    await using var failureBody = new MemoryStream();
    failureContext.Response.Body = failureBody;
    await PortfolioFailureResponse.Write(failureContext);
    using var failureDocument = JsonDocument.Parse(failureBody.ToArray());
    var failureMessage = failureDocument.RootElement.GetProperty("error").GetString();
    Check(failureContext.Response.StatusCode == 503, "unexpected failures preserve service-unavailable status");
    Check(
        failureContext.Response.ContentType?.StartsWith("application/json", StringComparison.Ordinal) == true,
        "failure body remains JSON"
    );
    Check(
        failureMessage
            == (
                readOnly
                    ? "Portfolio data is unavailable. Refresh to try again."
                    : "The action may have completed. Refresh status before taking another action."
            ),
        "read/write failure guidance " + method
    );
    Check(
        !failureDocument.RootElement.ToString().Contains("do-not-leak", StringComparison.Ordinal),
        "generic failures never echo request secrets"
    );
}

Console.WriteLine("Portfolio API security and failure-response checks passed without SQL or network.");

static void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(name);
    }
}
