using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure.Trading.Jobs;

public sealed class TradingDashboardAuthorizationFilter(TradingControlOptions options) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if ((!HttpMethods.IsGet(http.Request.Method) && !IsStatistics(http.Request)) || !IsSafeOrigin(http.Request))
        {
            return false;
        }

        var expected = options.OwnerToken;
        var supplied = http.Request.Headers["X-Quantara-Trading-Control"].ToString();
        return !string.IsNullOrWhiteSpace(expected)
            && supplied.Length is > 0 and <= 4096
            && http.Request.Headers["Sec-Fetch-Site"] != "cross-site"
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected))
            );
    }

    public static bool IsStatistics(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && (
            request.Path == "/api/trading/jobs/stats"
            || request.PathBase == "/api/trading/jobs" && request.Path == "/stats"
        )
        && string.Equals(
            request.ContentType?.Split(';', 2)[0].Trim(),
            "application/x-www-form-urlencoded",
            StringComparison.OrdinalIgnoreCase
        );

    private static bool IsSafeOrigin(HttpRequest request) =>
        string.IsNullOrEmpty(request.Headers.Origin.ToString())
        || string.Equals(
            request.Headers.Origin.ToString(),
            $"{request.Scheme}://{request.Host}",
            StringComparison.OrdinalIgnoreCase
        );
}
