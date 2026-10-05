using System.Net;
using System.Text;
using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class OpenAiTradingAgentGatewayTests
{
    [Fact]
    public async Task Sends_exact_responses_request_and_parses_message_output()
    {
        const string response = """
            {"status":"completed","id":"resp_123","model":"gpt-returned","output":[
              {"type":"reasoning"},
              {"type":"message","content":[{"type":"refusal","refusal":"ignored"},{"type":"output_text","text":"{\"action\":\"HOLD\"}"}]}
            ],"usage":{"input_tokens":12,"output_tokens":7}}
            """;
        using var handler = Handler.Respond(HttpStatusCode.OK, response);
        using var client = Client(handler);
        var result = await Gateway(client).CompleteStructuredAsync(Request(), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.openai.com/v1/responses", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("top-secret", handler.Request.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("gpt-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("system", body.RootElement.GetProperty("instructions").GetString());
        Assert.Equal("{\"evidence\":1}", body.RootElement.GetProperty("input").GetString());
        Assert.Equal(321, body.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("high", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        var format = body.RootElement.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("trading_agent_output", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.Equal("object", format.GetProperty("schema").GetProperty("type").GetString());
        Assert.DoesNotContain("top-secret", handler.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("tools", handler.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("conversation", handler.Body!, StringComparison.Ordinal);
        Assert.Equal("{\"action\":\"HOLD\"}", result.OutputJson);
        Assert.Equal("resp_123", result.ProviderRequestId);
        Assert.Equal("gpt-returned", result.Model);
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(7, result.OutputTokens);
        Assert.True(result.Latency >= TimeSpan.Zero);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("reasoning")]
    [InlineData("prompt")]
    [InlineData("system")]
    [InlineData("input")]
    public async Task Empty_required_request_text_fails_before_http(string field)
    {
        using var handler = Handler.Respond(HttpStatusCode.OK, ValidResponse());
        using var client = Client(handler);
        var request = field switch
        {
            "role" => Request() with { Role = " " },
            "reasoning" => Request() with { ReasoningEffort = "\t" },
            "prompt" => Request() with { PromptVersion = "" },
            "system" => Request() with { SystemInstructions = " " },
            _ => Request() with { InputJson = "" },
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Gateway(client).CompleteStructuredAsync(request, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Empty_model_is_unavailable_before_http()
    {
        using var handler = Handler.Respond(HttpStatusCode.OK, ValidResponse()); using var client = Client(handler);
        await Assert.ThrowsAsync<AgentModelUnavailableException>(() => Gateway(client).CompleteStructuredAsync(Request() with { Model = "" }, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Empty_api_key_is_unavailable_before_http()
    {
        using var handler = Handler.Respond(HttpStatusCode.OK, ValidResponse()); using var client = Client(handler);
        await Assert.ThrowsAsync<AgentModelUnavailableException>(() => Gateway(client, " ").CompleteStructuredAsync(Request(), CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("schema-empty")]
    [InlineData("schema-array")]
    [InlineData("input")]
    [InlineData("tokens")]
    [InlineData("timeout")]
    public async Task Invalid_structured_request_fails_before_http(string problem)
    {
        using var handler = Handler.Respond(HttpStatusCode.OK, ValidResponse()); using var client = Client(handler);
        var request = problem switch
        {
            "schema" => Request() with { JsonSchema = "{" },
            "schema-empty" => Request() with { JsonSchema = " " },
            "schema-array" => Request() with { JsonSchema = "[]" },
            "input" => Request() with { InputJson = "{" },
            "tokens" => Request() with { MaxOutputTokens = 0 },
            _ => Request() with { Timeout = TimeSpan.Zero },
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Gateway(client).CompleteStructuredAsync(request, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Non_success_response_is_sanitized()
    {
        using var handler = Handler.Respond(HttpStatusCode.BadRequest, "sensitive-provider-body"); using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<AgentProviderException>(() => Gateway(client).CompleteStructuredAsync(Request(), CancellationToken.None));
        Assert.DoesNotContain("sensitive-provider-body", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exhausted_credit_is_classified_without_leaking_provider_body()
    {
        const string body = "{\"error\":{\"message\":\"sensitive-provider-body\",\"type\":\"insufficient_quota\",\"code\":\"credit_balance_exhausted\"}}";
        using var handler = Handler.Respond(HttpStatusCode.TooManyRequests, body); using var client = Client(handler);

        var exception = await Assert.ThrowsAsync<AgentQuotaUnavailableException>(() =>
            Gateway(client).CompleteStructuredAsync(Request(), CancellationToken.None));

        Assert.Equal("Hosted Agent API credits are unavailable.", exception.Message);
        Assert.DoesNotContain("sensitive-provider-body", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_failure_is_sanitized()
    {
        using var handler = Handler.Throw(new HttpRequestException("top-secret sensitive-provider-body")); using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<AgentProviderException>(() => Gateway(client).CompleteStructuredAsync(Request(), CancellationToken.None));
        Assert.DoesNotContain("top-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-provider-body", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Response_stream_failure_is_sanitized_as_transport_failure()
    {
        using var handler = Handler.Throw(new IOException("top-secret sensitive-provider-body")); using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<AgentProviderException>(() => Gateway(client).CompleteStructuredAsync(Request(), CancellationToken.None));
        Assert.DoesNotContain("top-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-provider-body", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_reasoning_effort_fails_before_http()
    {
        using var handler = Handler.Respond(HttpStatusCode.OK, ValidResponse()); using var client = Client(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => Gateway(client).CompleteStructuredAsync(
            Request() with { ReasoningEffort = "invented" }, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"r\",\"model\":\"m\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"incomplete\",\"id\":\"r\",\"model\":\"m\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"model\":\"m\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"\",\"model\":\"m\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"r\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"r\",\"model\":\"\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"r\",\"model\":\"m\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"r\",\"model\":\"m\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"\"}]}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"r\",\"model\":\"m\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{}\"}]}]}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"r\",\"model\":\"m\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{}\"}]}],\"usage\":{\"input_tokens\":-1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"r\",\"model\":\"m\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{}\"}]}],\"usage\":{\"input_tokens\":1,\"output_tokens\":-1}}")]
    [InlineData("{\"status\":\"completed\",\"id\":\"r\",\"model\":\"m\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{}\"}]}],\"usage\":{\"input_tokens\":1.5,\"output_tokens\":1}}")]
    public async Task Malformed_or_incomplete_response_is_sanitized(string response)
    {
        using var handler = Handler.Respond(HttpStatusCode.OK, response); using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<AgentResponseFormatException>(() => Gateway(client).CompleteStructuredAsync(Request(), CancellationToken.None));
        Assert.DoesNotContain(response, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Internal_timeout_is_sanitized()
    {
        using var handler = Handler.Delay(TimeSpan.FromSeconds(5)); using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<AgentProviderTimeoutException>(() => Gateway(client).CompleteStructuredAsync(
            Request() with { Timeout = TimeSpan.FromMilliseconds(20) }, CancellationToken.None));
        Assert.DoesNotContain("top-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        using var handler = Handler.Delay(TimeSpan.FromSeconds(5)); using var client = Client(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gateway(client).CompleteStructuredAsync(Request(), cancellation.Token));
        Assert.IsNotType<AgentProviderTimeoutException>(exception);
    }

    private static AgentModelRequest Request() => new(
        "PortfolioExitAgent", "gpt-test", "high", "exit-v1", "system", "{\"evidence\":1}",
        // Parsing tests do not measure latency; timeout behavior has its own short deadline.
        "{\"type\":\"object\",\"additionalProperties\":false}", 321, TimeSpan.FromSeconds(30));
    private static OpenAiTradingAgentGateway Gateway(HttpClient client, string apiKey = "top-secret") => new(client, apiKey);
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.openai.com/") };
    private static string ValidResponse() => "{\"status\":\"completed\",\"id\":\"resp_1\",\"model\":\"gpt-test\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{}\"}]}],\"usage\":{\"input_tokens\":2,\"output_tokens\":3}}";

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback;
        private Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) => this.callback = callback;
        public int Calls { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public static Handler Respond(HttpStatusCode status, string body) => new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
        public static Handler Throw(Exception exception) => new((_, _) => Task.FromException<HttpResponseMessage>(exception));
        public static Handler Delay(TimeSpan delay) => new(async (_, token) => { await Task.Delay(delay, token); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ValidResponse()) }; });
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await callback(request, cancellationToken);
        }
    }
}
