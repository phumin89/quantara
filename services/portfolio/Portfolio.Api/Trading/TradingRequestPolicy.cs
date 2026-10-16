using System.Security.Cryptography;
using System.Text;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Api;

public static class TradingRequestPolicy
{
    public static async Task<bool> BodyWithinLimit(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering(8192, 8193);
        var buffer = new byte[4096];
        var size = 0;
        try
        {
            for (; ; )
            {
                var read = await request.Body.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, 8193 - size)),
                    cancellationToken
                );
                if (read == 0)
                {
                    return true;
                }

                size += read;
                if (size > 8192)
                {
                    return false;
                }
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413)
        {
            return false;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    public static bool Allows(HttpRequest request, string? internalHost, TradingControlOptions options)
    {
        var localHost =
            request.Host.Host is "127.0.0.1" or "localhost" or "[::1]"
            || !string.IsNullOrWhiteSpace(internalHost)
                && string.Equals(request.Host.Host, internalHost, StringComparison.OrdinalIgnoreCase);
        var origin = request.Headers.Origin.ToString();
        if (
            !localHost
            || origin is not ("" or "http://127.0.0.1:3000" or "http://localhost:3000")
            || request.Headers["Sec-Fetch-Site"] == "cross-site"
        )
        {
            return false;
        }

        if (HttpMethods.IsGet(request.Method))
        {
            return true;
        }

        var token = request.Headers["X-Quantara-Trading-Control"];
        return HttpMethods.IsPost(request.Method)
            && request
                .ContentType?.Split(';', 2)[0]
                .Trim()
                .Equals("application/json", StringComparison.OrdinalIgnoreCase) == true
            && token.Count == 1
            && !string.IsNullOrWhiteSpace(options.OwnerToken)
            && token.ToString().Length <= 4096
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(token.ToString())),
                SHA256.HashData(Encoding.UTF8.GetBytes(options.OwnerToken))
            );
    }
}
