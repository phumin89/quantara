using TradingCompany.Application;
using TradingCompany.Domain;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TradingCompany.Api;

public sealed class LiveExecutionClient : ILiveCompanyClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient transport;
    private readonly string coordinatorToken;
    private readonly LiveAccountId boundAccount;
    private readonly TimeProvider clock;

    public LiveExecutionClient(HttpClient transport, string coordinatorToken, LiveAccountId boundAccount, TimeProvider? clock = null)
    {
        this.transport = transport;
        this.coordinatorToken = coordinatorToken;
        this.boundAccount = boundAccount;
        this.clock = clock ?? TimeProvider.System;
        transport.Timeout = TimeSpan.FromSeconds(10);
        transport.MaxResponseContentBufferSize = 1024 * 1024;
    }

    // Production registration must use this handler with normal TLS validation. No owner
    // cookies or broker credentials belong in the advisory transport.
    public static HttpClient CreateTransport() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    public async Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken)
    {
        EnsureBinding(accountId);
        var packet = await Send<LiveResearchPacket>(HttpMethod.Get, $"research/{accountId.Value:N}", null, cancellationToken);
        if (packet.AccountContext != new TradingAccountContext(TradingMode.Live, accountId.Value.ToString("N")) ||
            packet.Snapshot is null || packet.Snapshot.AccountId != accountId) throw Unavailable();
        return packet;
    }

    public async Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken)
    {
        EnsureBinding(intent.AccountId);
        if (intent.Mode != TradingMode.Live) throw Unavailable();
        var receipt = await Send<LiveIntentReceipt>(HttpMethod.Post, "intents", intent, cancellationToken);
        if (receipt.Intent != intent || !Enum.IsDefined(receipt.State) || receipt.Violations is null ||
            receipt.State is LiveOrderState.Proposed or LiveOrderState.Reserved or LiveOrderState.Dispatching) throw Unavailable();
        return receipt;
    }

    public async Task<LiveTradePlanReceipt> RegisterPlanAsync(LiveTradePlanRequest plan, CancellationToken cancellationToken)
    {
        EnsureBinding(plan.AccountId);
        var receipt = await Send<LiveTradePlanReceipt>(HttpMethod.Post, "plans", plan, cancellationToken);
        if (receipt.Id != plan.Id || receipt.Violations is null || receipt.Accepted == (receipt.Violations.Count > 0)) throw Unavailable();
        return receipt;
    }

    private void EnsureBinding(LiveAccountId accountId)
    {
        if (boundAccount.Value == Guid.Empty || accountId != boundAccount || coordinatorToken is null ||
            coordinatorToken.Length is < 32 or > 256 || coordinatorToken.Any(char.IsControl)) throw Unavailable();
    }

    public async Task SaveMeetingAsync(DailyMeetingAttempt attempt, CancellationToken cancellationToken)
    {
        EnsureBinding(boundAccount);
        LiveMeetingReports.Validate(attempt, boundAccount, DateTimeOffset.UtcNow);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost:7443/internal/live/meetings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", coordinatorToken);
        request.Content = JsonContent.Create(attempt, options: Json);
        using var response = await transport.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw Unavailable();
    }

    public async Task SaveActivityAsync(LiveCompanyActivity activity, CancellationToken cancellationToken)
    {
        EnsureBinding(boundAccount);
        if (!LiveActivityValidation.IsValid(activity, boundAccount, clock.GetUtcNow())) throw Unavailable();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost:7443/internal/live/activity");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", coordinatorToken);
        request.Content = JsonContent.Create(activity, options: Json);
        using var response = await transport.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw Unavailable();
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? intent, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, "https://localhost:7443/internal/live/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", coordinatorToken);
            if (intent is not null) request.Content = JsonContent.Create(intent, options: Json);
            using var response = await transport.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK) throw Unavailable();
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken) ?? throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw Unavailable(); }
    }

    private static InvalidDataException Unavailable() => new("Live executor response is unavailable or invalid.");
}
