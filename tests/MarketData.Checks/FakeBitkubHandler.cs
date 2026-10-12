using System.Net;

namespace Quantara.MarketData.Checks;

public sealed class FakeBitkubHandler(string symbols, string ticker) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var payload = request.RequestUri?.AbsolutePath.EndsWith("symbols", StringComparison.Ordinal) == true ? symbols : ticker;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json") });
    }
}
