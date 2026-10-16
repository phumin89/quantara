using System.Text.Json;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class PreparedTradingCancellation(HttpClient http, HttpRequestMessage request) : IPreparedOrderSubmission
{
    private bool sent;

    public async Task<OrderBrokerResult> Send(CancellationToken cancellationToken)
    {
        if (this.sent)
        {
            throw new InvalidOperationException("Cancellation was already dispatched.");
        }

        this.sent = true;
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return new OrderBrokerResult(
            document.RootElement.GetProperty("error").GetInt32() == 0,
            null,
            "Cancellation response received; broker reconciliation is required."
        );
    }

    public void Dispose() => request.Dispose();
}
