using Microsoft.AspNetCore.Http;

namespace Quantara.Portfolio.Api;

public static class PortfolioFailureResponse
{
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
