using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TradingCompany.Infrastructure;

public sealed partial class BitkubLiveBroker
{
    // Server-only observer. The callback persists sanitized receipts before reading the next event.
    // No placement, cancellation, retries of orders, raw responses or authentication logging.
    public async Task ObserveOrdersAsync(Func<BitkubOrderReceipt, CancellationToken, Task> persist,
        Action<bool> ready, CancellationToken cancellationToken)
    {
        ready(false);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("User-Agent", "dotnet-quantara/1.0");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(110)); // reconnect before the published two-hour limit
        var token = lifetime.Token;
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
            connect.CancelAfter(TimeSpan.FromSeconds(25));
            using var timeRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.bitkub.com/api/v3/servertime");
            using var time = await Read(timeRequest, connect.Token);
            var timestamp = time.RootElement.GetInt64();
            if (timestamp <= 0 || Math.Abs((decimal)clock.GetUtcNow().ToUnixTimeMilliseconds() - timestamp) > 30000) throw Unavailable();
            var stamp = timestamp.ToString(CultureInfo.InvariantCulture);
            var signature = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(stamp)));
            await socket.ConnectAsync(new("wss://stream.bitkub.com/v3/private"), connect.Token);
            await Send(new { @event = "auth", data = new Dictionary<string, string>
                { ["X-BTK-APIKEY"] = apiKey, ["X-BTK-SIGN"] = signature, ["X-BTK-TIMESTAMP"] = stamp } }, connect.Token);
            using (var auth = await Receive(connect.Token))
                if (Required(auth.RootElement, "event").GetString() != "auth" || Required(auth.RootElement, "code").GetString() != "200") throw Unavailable();
            await Send(new { @event = "subscribe", channel = "order_update" }, connect.Token);
            while (true)
            {
                using var reply = await Receive(connect.Token);
                var root = reply.RootElement;
                if (Required(root, "event").GetString() == "order_update") { await Persist(root); continue; }
                if (Required(root, "event").GetString() != "subscribe" || Required(root, "code").GetString() != "200" ||
                    Required(root, "channel").GetString() != "order_update") throw Unavailable();
                break;
            }
            ready(true);
            var read = Receive(token);
            var ping = Task.Delay(TimeSpan.FromMinutes(4), token);
            var waitingForPong = false;
            while (!token.IsCancellationRequested)
            {
                if (await Task.WhenAny(read, ping) == ping)
                {
                    await ping;
                    if (waitingForPong) throw Unavailable();
                    await Send(new { @event = "ping" }, token);
                    waitingForPong = true;
                    ping = Task.Delay(TimeSpan.FromSeconds(20), token);
                    continue;
                }
                using var message = await read;
                var root = message.RootElement;
                if (Required(root, "code").GetString() != "200") throw Unavailable();
                switch (Required(root, "event").GetString())
                {
                    case "order_update": await Persist(root); break;
                    case "ping":
                        waitingForPong = false;
                        ping = Task.Delay(TimeSpan.FromMinutes(4), token);
                        break;
                    default: throw Unavailable();
                }
                read = Receive(token);
            }
        }
        finally { ready(false); lifetime.Cancel(); socket.Abort(); }

        async Task Persist(JsonElement root)
        {
            if (Required(root, "code").GetString() != "200") throw Unavailable();
            var receipt = BitkubOrderReceipts.Parse(Required(root, "data"));
            if (receipt is null) return;
            if (receipt.UpdatedAt > clock.GetUtcNow().ToUnixTimeMilliseconds() + 5000) throw Unavailable();
            await persist(receipt, token);
        }
        async Task Send(object value, CancellationToken ct)
            => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value), WebSocketMessageType.Text, true, ct);
        async Task<JsonDocument> Receive(CancellationToken ct)
        {
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                if (received.MessageType != WebSocketMessageType.Text || bytes.Length + received.Count > 65536) throw Unavailable();
                bytes.Write(buffer, 0, received.Count);
                if (received.EndOfMessage) return JsonDocument.Parse(bytes.ToArray());
            }
        }
    }
}
