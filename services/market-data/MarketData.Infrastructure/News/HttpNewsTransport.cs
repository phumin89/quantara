using System.Net;
using System.Net.Sockets;

namespace Quantara.MarketData.Infrastructure.News;

public sealed class HttpNewsTransport : INewsTransport
{
    private readonly HttpClient client;

    public HttpNewsTransport(HttpClient client)
    {
        this.client = client;
    }

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = ConnectPublicEndpoint,
        };
    }

    private static async ValueTask<Stream> ConnectPublicEndpoint(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!NewsNetworkPolicy.IsAllowedEndpoint(context.DnsEndPoint.Host, context.DnsEndPoint.Port))
        {
            throw new HttpRequestException("News endpoint is not allowlisted.");
        }

        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !NewsNetworkPolicy.IsPublic(address)))
        {
            throw new HttpRequestException("News endpoint resolved to a private address.");
        }

        var selected =
            addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
        var socket = new Socket(selected.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(selected, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<NewsTransportResponse> Fetch(
        Uri uri,
        string? etag,
        string? lastModified,
        CancellationToken cancellationToken
    )
    {
        if (
            !NewsNetworkPolicy.IsAllowedEndpoint(uri.Host, uri.Port)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
        )
        {
            throw new InvalidDataException("News source must use HTTPS.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        if (!string.IsNullOrWhiteSpace(lastModified))
        {
            request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
        }

        using var response = await this.client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        await response.Content.LoadIntoBufferAsync(512_000, cancellationToken);
        var body =
            response.StatusCode == System.Net.HttpStatusCode.NotModified
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);
        return new NewsTransportResponse(
            (int)response.StatusCode,
            body,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified?.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            response.Headers.RetryAfter?.ToString()
        );
    }
}
