using System.Net.Http.Json;
using System.Text.Json;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingNotifier(HttpClient http, TelegramSettings? settings) : ITradingNotifier
{
    public bool IsConfigured => settings is { Token.Length: > 0, ChatId: not 0 };

    public async Task<long> Send(string text, CancellationToken cancellationToken)
    {
        using var response = await this.Call(
            "sendMessage",
            new
            {
                chat_id = settings?.ChatId,
                text,
                protect_content = true,
            },
            cancellationToken
        );
        return response.RootElement.GetProperty("result").GetProperty("message_id").GetInt64();
    }

    public async Task Edit(long messageId, string text, CancellationToken cancellationToken)
    {
        using var response = await this.Call(
            "editMessageText",
            new
            {
                chat_id = settings?.ChatId,
                message_id = messageId,
                text,
            },
            cancellationToken
        );
    }

    private async Task<JsonDocument> Call(string method, object body, CancellationToken cancellationToken)
    {
        if (!this.IsConfigured)
        {
            throw new InvalidOperationException("Trading notifications are not configured.");
        }

        try
        {
            using var response = await http.PostAsJsonAsync(
                "/bot" + settings?.Token + "/" + method,
                body,
                cancellationToken
            );
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (text.Length > 256000)
            {
                throw new InvalidDataException("Trading notification response exceeds its budget.");
            }

            var document = JsonDocument.Parse(text);
            if (response.IsSuccessStatusCode && document.RootElement.GetProperty("ok").GetBoolean())
            {
                return document;
            }

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
            throw new InvalidOperationException("Trading notification was not acknowledged.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "Trading notification was cancelled; its outcome remains uncertain.",
                cancellationToken
            );
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "Trading notification outcome is uncertain; automatic resend is disabled."
            );
        }
    }
}
