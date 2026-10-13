using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TelegramTransport(HttpClient http, TelegramSettings settings) : ITelegramTransport
{
    private static readonly string[] allowedUpdates = ["callback_query"];

    public async Task<long> Send(OrderProposal proposal, string token, CancellationToken cancellationToken)
    {
        using var response = await this.Call(
            "sendMessage",
            new
            {
                chat_id = settings.ChatId,
                text = TelegramTransport.Text(proposal, "⏳ Awaiting your approval"),
                parse_mode = "HTML",
                protect_content = true,
                reply_markup = new
                {
                    inline_keyboard = new[]
                    {
                        new[]
                        {
                            new { text = "✅ Approve", callback_data = "a:" + token },
                            new { text = "✖ Reject", callback_data = "r:" + token },
                        },
                    },
                },
            },
            cancellationToken
        );
        return response.RootElement.GetProperty("result").GetProperty("message_id").GetInt64();
    }

    public async Task<IReadOnlyList<TelegramClick>> Poll(long offset, CancellationToken cancellationToken)
    {
        using var response = await this.Call(
            "getUpdates",
            new
            {
                offset,
                timeout = 5,
                limit = 10,
                allowed_updates = TelegramTransport.allowedUpdates,
            },
            cancellationToken
        );
        var clicks = new List<TelegramClick>();
        foreach (var update in response.RootElement.GetProperty("result").EnumerateArray())
        {
            if (
                !update.TryGetProperty("update_id", out var identifier)
                || !identifier.TryGetInt64(out var updateId)
                || updateId < 0
                || updateId == long.MaxValue
            )
            {
                continue;
            }

            var click = new TelegramClick(updateId, "", 0, 0, 0, "");
            try
            {
                if (
                    update.TryGetProperty("callback_query", out var callback)
                    && callback.TryGetProperty("message", out var message)
                    && message.TryGetProperty("chat", out var chat)
                    && chat.GetProperty("type").GetString() == "private"
                )
                {
                    var data = callback.GetProperty("data").GetString() ?? "";
                    var callbackId = callback.GetProperty("id").GetString() ?? "";
                    if (data.Length <= 64 && callbackId.Length is > 0 and <= 200)
                    {
                        click = new(
                            updateId,
                            callbackId,
                            callback.GetProperty("from").GetProperty("id").GetInt64(),
                            chat.GetProperty("id").GetInt64(),
                            message.GetProperty("message_id").GetInt64(),
                            data
                        );
                    }
                }
            }
            catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // Malformed/unrelated updates are acknowledged without review authority.
            }

            clicks.Add(click);
        }

        return clicks;
    }

    public async Task Answer(string callbackId, string text, CancellationToken cancellationToken)
    {
        using var response = await this.Call(
            "answerCallbackQuery",
            new { callback_query_id = callbackId, text },
            cancellationToken
        );
    }

    public async Task Edit(long messageId, OrderProposal proposal, string status, CancellationToken cancellationToken)
    {
        using var response = await this.Call(
            "editMessageText",
            new
            {
                chat_id = settings.ChatId,
                message_id = messageId,
                text = TelegramTransport.Text(proposal, WebUtility.HtmlEncode(status)),
                parse_mode = "HTML",
                reply_markup = new { inline_keyboard = Array.Empty<object>() },
            },
            cancellationToken
        );
    }

    private async Task<JsonDocument> Call(string method, object body, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "/bot" + settings.Token + "/" + method,
                body,
                cancellationToken
            );
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (json.Length > 256000)
            {
                throw new InvalidDataException("Telegram response too large.");
            }

            var document = JsonDocument.Parse(json);
            if (!response.IsSuccessStatusCode || document.RootElement.GetProperty("ok").GetBoolean() != true)
            {
                if (
                    method == "editMessageText"
                    && document.RootElement.TryGetProperty("description", out var description)
                    && (description.GetString() ?? "").StartsWith(
                        "Bad Request: message is not modified",
                        StringComparison.Ordinal
                    )
                )
                {
                    return document;
                }

                document.Dispose();
                throw new InvalidOperationException("Telegram request failed.");
            }

            return document;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Raw request exceptions can contain the bot token in their URL.
            throw new InvalidOperationException("Telegram is unavailable. No automatic order retry is authorized.");
        }
    }

    public static string Text(OrderProposal proposal, string status)
    {
        var buy = proposal.Side == TradeSide.Buy;
        var amount = proposal.Amount.ToString("#,0.############################", CultureInfo.InvariantCulture);
        var price = proposal.LimitPrice.ToString("#,0.############################", CultureInfo.InvariantCulture);
        var reason = WebUtility.HtmlEncode(proposal.Reason);
        var deadline = proposal
            .ExpiresAt.ToOffset(TimeSpan.FromHours(7))
            .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"💱 <b>REAL ORDER</b>\n\n<b>{(buy ? "Buy" : "Sell")} {WebUtility.HtmlEncode(proposal.Symbol)}</b> · {(buy ? "฿" : "")}{amount}{(buy ? "" : " " + WebUtility.HtmlEncode(proposal.Symbol))}\nLimit · ฿{price} / coin\nEst. fee · ฿{proposal.EstimatedFeeThb.ToString("0.00", CultureInfo.InvariantCulture)} (0.25% baseline)\n\n{reason}\n\n{status}\nApproval expires · {deadline} Bangkok\nApprove submits this limit order to Bitkub.\nApproval expiry does not cancel an open order.";
    }
}
