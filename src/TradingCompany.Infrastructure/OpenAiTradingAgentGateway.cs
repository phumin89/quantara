using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TradingCompany.Application;

namespace TradingCompany.Infrastructure;

public sealed class OpenAiTradingAgentGateway : ITradingAgentGateway
{
    private readonly HttpClient client;
    private readonly string apiKey;

    public OpenAiTradingAgentGateway(HttpClient client, string apiKey)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.apiKey = apiKey;
    }

    public async Task<AgentModelResponse> CompleteStructuredAsync(
        AgentModelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new AgentModelUnavailableException("Hosted Agent model is unavailable.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AgentModelUnavailableException("Hosted Agent credentials are unavailable.");

        ValidateRequired(request.Role, nameof(request.Role));
        ValidateRequired(request.ReasoningEffort, nameof(request.ReasoningEffort));
        if (request.ReasoningEffort is not ("none" or "minimal" or "low" or "medium" or "high" or "xhigh"))
            throw new ArgumentException("Reasoning effort is unsupported.", nameof(request.ReasoningEffort));
        ValidateRequired(request.PromptVersion, nameof(request.PromptVersion));
        ValidateRequired(request.SystemInstructions, nameof(request.SystemInstructions));
        ValidateRequired(request.InputJson, nameof(request.InputJson));
        ValidateRequired(request.JsonSchema, nameof(request.JsonSchema));
        if (request.MaxOutputTokens <= 0) throw new ArgumentOutOfRangeException(nameof(request.MaxOutputTokens));
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request.Timeout));

        ValidateJson(request.InputJson, nameof(request.InputJson), requireObject: false);
        using var schemaDocument = ValidateJson(request.JsonSchema, nameof(request.JsonSchema), requireObject: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        var body = new
        {
            model = request.Model,
            instructions = request.SystemInstructions,
            input = request.InputJson,
            max_output_tokens = request.MaxOutputTokens,
            reasoning = new { effort = request.ReasoningEffort },
            store = false,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "trading_agent_output",
                    strict = true,
                    schema = schemaDocument.RootElement,
                },
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                await ThrowProviderFailureAsync(response, timeout.Token).ConfigureAwait(false);

            try
            {
                await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(
                    responseStream, cancellationToken: timeout.Token).ConfigureAwait(false);
                stopwatch.Stop();
                return ParseCompletedResponse(document.RootElement, stopwatch.Elapsed);
            }
            catch (JsonException)
            {
                throw new AgentResponseFormatException("Hosted Agent response was malformed.");
            }
            catch (InvalidOperationException)
            {
                throw new AgentResponseFormatException("Hosted Agent response was malformed.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new AgentProviderTimeoutException("Hosted Agent provider request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new AgentProviderException("Hosted Agent provider transport failed.");
        }
        catch (IOException)
        {
            throw new AgentProviderException("Hosted Agent provider transport failed.");
        }
    }

    private static async Task ThrowProviderFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("error", out var error) &&
                    error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("code", out var code) &&
                    code.ValueKind == JsonValueKind.String &&
                    string.Equals(code.GetString(), "credit_balance_exhausted", StringComparison.Ordinal))
                    throw new AgentQuotaUnavailableException("Hosted Agent API credits are unavailable.");
            }
            catch (AgentQuotaUnavailableException) { throw; }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
            {
                // Provider bodies are intentionally not surfaced.
            }
        }
        throw new AgentProviderException($"Hosted Agent provider returned HTTP {(int)response.StatusCode}.");
    }

    private static AgentModelResponse ParseCompletedResponse(JsonElement root, TimeSpan latency)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String ||
            !string.Equals(status.GetString(), "completed", StringComparison.Ordinal))
            throw new AgentResponseFormatException("Hosted Agent response was incomplete.");

        var id = RequiredNonemptyString(root, "id");
        var model = RequiredNonemptyString(root, "model");
        var output = FindOutputText(root);
        if (string.IsNullOrWhiteSpace(output))
            throw new AgentResponseFormatException("Hosted Agent response contained no structured output.");

        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            throw new AgentResponseFormatException("Hosted Agent response usage was missing.");
        var inputTokens = RequiredNonnegativeInt(usage, "input_tokens");
        var outputTokens = RequiredNonnegativeInt(usage, "output_tokens");
        return new AgentModelResponse(output, id, model, inputTokens, outputTokens, latency);
    }

    private static string? FindOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("type", out var itemType) ||
                itemType.ValueKind != JsonValueKind.String ||
                !string.Equals(itemType.GetString(), "message", StringComparison.Ordinal) ||
                !item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array) continue;

            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object &&
                    part.TryGetProperty("type", out var partType) &&
                    partType.ValueKind == JsonValueKind.String &&
                    string.Equals(partType.GetString(), "output_text", StringComparison.Ordinal) &&
                    part.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                    return text.GetString();
            }
        }
        return null;
    }

    private static string RequiredNonemptyString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new AgentResponseFormatException("Hosted Agent response metadata was missing.");
        return value.GetString()!;
    }

    private static int RequiredNonnegativeInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result) || result < 0)
            throw new AgentResponseFormatException("Hosted Agent response usage was invalid.");
        return result;
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Request field is required.", parameterName);
    }

    private static JsonDocument ValidateJson(string value, string parameterName, bool requireObject)
    {
        try
        {
            var document = JsonDocument.Parse(value);
            if (requireObject && document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new ArgumentException("JSON schema must be an object.", parameterName);
            }
            return document;
        }
        catch (JsonException)
        {
            throw new ArgumentException("Request field must contain valid JSON.", parameterName);
        }
    }
}
