namespace TradingCompany.LiveExecution;

// Public HTTP entry only: no proxying, account data, cookies or financial actions.
public static class OwnerLaunchRedirect
{
    public static void Handle(HttpContext context)
    {
        var host = context.Request.Host;
        var path = context.Request.Path.Value;
        if (context.Request.IsHttps ||
            !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) ||
            !host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && host.Host != "127.0.0.1" ||
            host.Port is not (null or 2080) || path is not ("/" or "/account" or "/trading"))
        { context.Response.StatusCode = 403; return; }
        context.Response.Headers.CacheControl = "no-store";
        // Fixed origin and known paths; never reflect Host, query, tokens or a return URL.
        context.Response.Redirect("https://localhost:3000" + path);
    }
}
