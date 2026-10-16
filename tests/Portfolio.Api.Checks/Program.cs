using System.Text;
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

Console.WriteLine("Portfolio API security checks passed without SQL or network.");

static void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(name);
    }
}
