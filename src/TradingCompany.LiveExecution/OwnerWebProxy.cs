namespace TradingCompany.LiveExecution;

// The public frontend runs without broker credentials. Only the owner host terminates TLS.
public static class OwnerWebProxy
{
    private static readonly string[] NavigationHeaders = ["RSC", "Next-Router-State-Tree", "Next-Router-Prefetch", "Next-Router-Segment-Prefetch", "Next-Url"];
    private static readonly string[] PublicResponseHeaders = ["Cache-Control", "Vary", "x-nextjs-stale-time", "x-nextjs-postponed", "x-nextjs-rewritten-path", "x-nextjs-rewritten-query", "x-nextjs-prerender"];

    public static async Task ForwardAsync(HttpContext context, HttpClient client)
    {
        if (!context.Request.IsHttps || context.Request.Host.Value != "localhost:3000")
        { context.Response.StatusCode = 403; return; }
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        { context.Response.StatusCode = 405; return; }
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
            (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ? "http://owner-web:3001" : "http://127.0.0.1:3001")
            + context.Request.Path + context.Request.QueryString);
        // Only the public Next navigation protocol crosses this boundary. Owner credentials never do.
        foreach (var name in NavigationHeaders)
        {
            if (!context.Request.Headers.TryGetValue(name, out var values)) continue;
            if (values.Count != 1 || values[0] is not { Length: > 0 and <= 8192 } value)
            { context.Response.StatusCode = 400; return; }
            request.Headers.TryAddWithoutValidation(name, value);
        }
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
            foreach (var name in PublicResponseHeaders)
                if (response.Headers.TryGetValues(name, out var values))
                    context.Response.Headers[name] = values.ToArray();
            // Next may correct a navigation cache key. Keep redirects on this public origin only.
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
            {
                var target = location.OriginalString;
                if (target.Length <= 8192 && target.StartsWith('/') && !target.StartsWith("//") && !target.Contains('\\'))
                    context.Response.Headers.Location = target;
            }
            if (!HttpMethods.IsHead(context.Request.Method))
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 503;
        }
    }
}
