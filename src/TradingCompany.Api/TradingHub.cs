using Microsoft.AspNetCore.SignalR;
using TradingCompany.Application;
namespace TradingCompany.Api;
public sealed class TradingHub : Hub { }
public sealed class SignalRRealtimeEventPublisher(IHubContext<TradingHub> hub) : IRealtimeEventPublisher
{
    public Task PublishAsync(RealtimeEvent message, CancellationToken cancellationToken)
    {
        if (!TradingAccountContext.IsValid(message.AccountContext) || message.AccountContext?.Mode == TradingCompany.Domain.TradingMode.Live)
            throw new UnauthorizedAccessException("Live events require the private account channel.");
        return hub.Clients.All.SendAsync("TradingEvent", message, cancellationToken);
    }
}
