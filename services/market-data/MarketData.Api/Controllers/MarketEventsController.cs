using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Api.Contracts;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Api.Controllers;

[ApiController]
[Route("api/market/events")]
public sealed class MarketEventsController(IMediator mediator, IConfiguration configuration) : ControllerBase
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<MarketEventKind>(JsonNamingPolicy.CamelCase, false) },
    };

    [HttpPost("watch")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Watch([FromBody] WatchRequest request, CancellationToken cancellationToken)
    {
        if (!this.IsAuthorized())
        {
            return this.Unauthorized();
        }

        try
        {
            await mediator.Send(new SetMarketWatchCommand(request.Symbols), cancellationToken);
            return this.Ok(new { watchedSymbols = request.Symbols });
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet]
    public async Task Get(CancellationToken cancellationToken)
    {
        if (!this.IsAuthorized())
        {
            this.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        this.Response.ContentType = "text/event-stream";
        this.Response.Headers.CacheControl = "no-store";
        this.Response.Headers["X-Accel-Buffering"] = "no";
        await this.Response.StartAsync(cancellationToken);
        await foreach (var item in await mediator.Send(new ReadMarketEventsQuery(), cancellationToken))
        {
            var json = JsonSerializer.Serialize(item, jsonOptions);
            await this.Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"), cancellationToken);
            await this.Response.Body.FlushAsync(cancellationToken);
        }
    }

    private bool IsAuthorized() =>
        this.Request.Headers.TryGetValue("X-Quantara-Market-Events", out var supplied)
        && supplied.Count == 1
        && MarketEventAccess.Allows(configuration["MarketEvents:Token"], supplied[0]);
}
