using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Api;

[ApiController]
[Route("api/trading")]
public sealed class TradingController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.Content(await mediator.Send(new GetTradingOverviewQuery(), cancellationToken), "application/json");

    [HttpGet("reports")]
    public async Task<IActionResult> Reports(CancellationToken cancellationToken) =>
        this.Content(await mediator.Send(new GetTradingReportsQuery(), cancellationToken), "application/json");

    [HttpPost("config")]
    public async Task<IActionResult> Configure(ConfigureTradingRequest request, CancellationToken cancellationToken) =>
        this.Content(
            await mediator.Send(new ConfigureTradingCommand(request.Rules), cancellationToken),
            "application/json"
        );

    [HttpPost("mode")]
    public async Task<IActionResult> Mode(SetTradingModeRequest request, CancellationToken cancellationToken)
    {
        var mode = request.Mode switch
        {
            "stopped" => TradingMode.Stopped,
            "paper" => TradingMode.Paper,
            "manualReview" => TradingMode.ManualReview,
            _ => throw new ArgumentException("Use the separate activation action to authorize live trading."),
        };
        return this.Content(
            await mediator.Send(new SetTradingModeCommand(mode), cancellationToken),
            "application/json"
        );
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(ActivateTradingRequest request, CancellationToken cancellationToken) =>
        this.Content(
            await mediator.Send(
                new ActivateTradingCommand(
                    request.AccountKey,
                    request.RulesHash,
                    request.ExpiresAt,
                    request.Acknowledgement
                ),
                cancellationToken
            ),
            "application/json"
        );

    [HttpPost("disable")]
    public async Task<IActionResult> Disable(CancellationToken cancellationToken) =>
        this.Content(await mediator.Send(new DisableTradingCommand(false), cancellationToken), "application/json");

    [HttpPost("emergency-stop")]
    public async Task<IActionResult> EmergencyStop(CancellationToken cancellationToken) =>
        this.Content(await mediator.Send(new DisableTradingCommand(true), cancellationToken), "application/json");
}
