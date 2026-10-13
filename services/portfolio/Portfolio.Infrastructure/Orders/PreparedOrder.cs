using System.Text.Json;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class PreparedOrder(HttpClient http, HttpRequestMessage request) : IPreparedOrderSubmission
{
    public void Dispose() => request.Dispose();

    public async Task<OrderBrokerResult> Send(CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var error = json.RootElement.GetProperty("error").GetInt32();
        if (error != 0)
        {
            return new(
                false,
                null,
                $"Bitkub rejected the order (code {error}). Review market rules, balance and Trade API permission."
            );
        }

        var orderId = json.RootElement.GetProperty("result").GetProperty("id").ToString();
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 200)
        {
            throw new InvalidDataException("The broker did not return an order identifier.");
        }

        return new(
            true,
            orderId,
            "Accepted by Bitkub. Submitted does not mean filled. Manage the open limit order in Bitkub; approval expiry does not cancel it."
        );
    }
}
