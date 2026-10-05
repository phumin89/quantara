using TradingCompany.Application;
using TradingCompany.Infrastructure;

namespace TradingCompany.Api;

public sealed class ForwardFundingService(IForwardFundingStore store, IForwardInvestmentStore investments, JsonForwardPaperStore paper, TimeProvider clock,
    IConfiguration configuration, ILogger<ForwardFundingService> logger) : BackgroundService
{
    private readonly object gate = new();
    private string? failure;
    public bool Enabled => configuration.GetValue<bool>("ForwardFunding:Enabled");

    public void Tick()
    {
        lock (gate)
        {
            if (!Enabled || failure is not null) return;
            try
            {
                var now = clock.GetUtcNow();
                var funding = store.Advance(now);
                var legacy = investments.Read();
                paper.Update(current => ForwardPaperFunding.Synchronize(current, funding, legacy, now,
                    configuration.GetValue<decimal>("ForwardFunding:InitialPaperCapital")));
            }
            catch (Exception ex)
            {
                failure = "Funding stopped: persisted account requires inspection. No replacement account was created.";
                logger.LogError(ex, "Forward funding stopped.");
            }
        }
    }

    public IResult GetStatus()
    {
        lock (gate)
        {
            if (failure is not null) return Results.Json(new { status = "UNAVAILABLE", error = failure }, statusCode: 503);
            try
            {
                var account = store.Read();
                var legacy = investments.Read();
                var execution = paper.Read();
                if (account is null && (legacy is not null || execution is not null))
                    throw new InvalidDataException("Investment portfolio has no source treasury.");
                if (account is not null) ForwardInvestmentReconciliation.Pending(account, legacy);
                var snapshot = execution is null ? null : ForwardPaperLedger.Snapshot(execution);
                var portfolio = snapshot is null ? null : new
                {
                    version = execution!.Version, fundingAccountId = execution.FundingAccountId,
                    snapshot.Cash, snapshot.TotalContributed, snapshot.TotalPnl, snapshot.Equity, snapshot.RealizedPnl,
                    snapshot.Drawdown, snapshot.Positions, snapshot.PendingOrders, snapshot.Receipts,
                    initialCapital = execution.Events.Sum(e => e.InitialCapital ?? 0m),
                    currency = "THB", tradingStatus = "PAPER_LEDGER_READY",
                    credits = execution.Events.Where(e => e.Credit is not null).Select(e => e.Credit).ToArray()
                };
                decimal? pendingInvestment = account is null ? null : execution is null ? account.InvestmentCash
                    : ForwardPaperFunding.Pending(execution, account);
                var status = !Enabled ? "DISABLED" : account is null ? "STARTING"
                    : portfolio is null ? "FUNDING_ONLY" : pendingInvestment > 0 ? "TRANSFER_PENDING"
                    : portfolio.Cash > 0 ? "PORTFOLIO_FUNDED" : "AWAITING_INVESTMENT_BUDGET";
                return Results.Ok(new { status, account, portfolio, pendingInvestment });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Forward account read failed.");
                return Results.Json(new { status = "UNAVAILABLE", error = "Cannot verify the saved forward account." }, statusCode: 503);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        do { Tick(); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
