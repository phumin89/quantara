using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class CodexCliTradingAgentGatewayTests
{
    [Fact]
    public async Task Invokes_ephemeral_read_only_cli_and_parses_completed_events()
    {
        IReadOnlyList<string>? args = null; string? input = null; string? schema = null;
        var gateway = new CodexCliTradingAgentGateway("codex", (a, d, i, _) =>
        {
            args = a; input = i; schema = a[Array.IndexOf(a.ToArray(), "--output-schema") + 1];
            Assert.True(File.Exists(schema));
            return Task.FromResult(new CodexCliProcessResult(0,
                "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"{\\\"action\\\":\\\"HOLD\\\"}\"}}\n" +
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":4,\"output_tokens\":2}}\n", ""));
        });
        var request = Request();
        var result = await gateway.CompleteStructuredAsync(request, CancellationToken.None);
        Assert.Equal("{\"action\":\"HOLD\"}", result.OutputJson);
        Assert.Contains("--json", args!); Assert.Contains("--ephemeral", args!); Assert.Contains("--sandbox", args!);
        Assert.Contains("read-only", args!); Assert.Contains("--output-schema", args!); Assert.DoesNotContain("-m", args!);
        Assert.Contains("exactly \"Role\"", input);
        Assert.Contains(request.InputJson, input);
        Assert.NotNull(schema);
    }

    [Fact]
    public async Task Rejects_success_without_turn_completed()
    {
        var gateway = new CodexCliTradingAgentGateway("codex", (_, _, _, _) => Task.FromResult(new CodexCliProcessResult(0,
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"{}\"}}", "")));
        await Assert.ThrowsAsync<AgentResponseFormatException>(() => gateway.CompleteStructuredAsync(Request(), CancellationToken.None));
    }

    private static AgentModelRequest Request() => new("Role", "codex-default", "high", "v1", "system", "{\"x\":1}", "{\"type\":\"object\"}", 100, TimeSpan.FromSeconds(2));
}
