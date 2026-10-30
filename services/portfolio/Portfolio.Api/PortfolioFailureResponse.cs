using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Quantara.Portfolio.Api;

public static class PortfolioFailureResponse
{
    private static readonly Action<ILogger, string, string, Exception?> logFailure = LoggerMessage.Define<
        string,
        string
    >(
        LogLevel.Error,
        new EventId(1, nameof(PortfolioFailureResponse)),
        "Portfolio request failed with {FailureType}. Trace {TraceId}."
    );

    public static void Log(ILogger logger, HttpContext context, Exception error) =>
        logFailure(logger, error.GetType().Name, context.TraceIdentifier, null);

    public static Task Write(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        var message =
            HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
                ? "Portfolio data is unavailable. Refresh to try again."
                : "The action may have completed. Refresh status before taking another action.";
        return context.Response.WriteAsJsonAsync(new { error = message });
    }
}
