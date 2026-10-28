using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class AutonomousRiskCacheChecks
{
    public static async Task Run()
    {
        await FrozenClock();
        await InFlightRead();
        await InvalidationWhileAuditPending();
        Console.WriteLine("Autonomous risk cache checks passed.");
    }

    private static async Task FrozenClock()
    {
        using var fixture = new AutonomousFixture();
        await fixture.Risk.Read(fixture.Rules, true, CancellationToken.None);
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with { Balances = [new Balance("THB", 2000, 2000)] };
        fixture.Risk.Invalidate();
        var rejected = await fixture.Risk.Size(
            Guid.NewGuid(),
            fixture.Rules,
            fixture.Book("ETH"),
            110m,
            new RiskSignal(99m, fixture.Book("ETH").Bars[^1].Timestamp, fixture.Time.Now, "cache-check"),
            false,
            CancellationToken.None
        );
        Check(
            !rejected.Decision.Accepted
                && rejected.Decision.Code == "unknownCapital"
                && rejected.Capital.SpendableThb is null
                && fixture.Evidence.ReadCalls == 1,
            "invalidated cache rejects stale capital"
        );
        var refreshed = await fixture.Risk.Read(fixture.Rules, false, CancellationToken.None);
        Check(
            refreshed.Snapshot.SpendableThb == 2000 && fixture.Evidence.ReadCalls == 2,
            "invalidated read fetches changed capital"
        );
        var recovered = await fixture.Risk.Size(
            Guid.NewGuid(),
            fixture.Rules,
            fixture.Book("ETH"),
            110m,
            new RiskSignal(99m, fixture.Book("ETH").Bars[^1].Timestamp, fixture.Time.Now, "cache-check"),
            false,
            CancellationToken.None
        );
        Check(
            recovered.Decision.Accepted
                && recovered.Capital.SpendableThb == 2000
                && fixture.Evidence.ReadCalls == 2
                && fixture.Store.State.Mode == TradingMode.Stopped,
            "fresh capital restores valid sizing without live authority"
        );
    }

    private static async Task InFlightRead()
    {
        using var fixture = new AutonomousFixture();
        await fixture.Risk.Read(fixture.Rules, true, CancellationToken.None);
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Evidence.BeforeRead = barrier;
        var old = fixture.Risk.Read(fixture.Rules, true, CancellationToken.None);
        try
        {
            Check(!old.IsCompleted && fixture.Evidence.ReadCalls == 2, "stale read remains in flight");
            fixture.Risk.Invalidate();
            fixture.Evidence.BeforeRead = null;
            fixture.Evidence.Evidence = fixture.Evidence.Evidence with { Balances = [new Balance("THB", 3000, 3000)] };
            var fresh = await fixture.Risk.Read(fixture.Rules, true, CancellationToken.None);
            Check(fresh.Snapshot.SpendableThb == 3000, "fresh read publishes new capital");
            fixture.Evidence.Evidence = fixture.Evidence.Evidence with { Balances = [new Balance("THB", 1000, 1000)] };
            barrier.TrySetResult();
            await RejectsInvalidation(old, "in-flight stale read is rejected");
        }
        finally
        {
            barrier.TrySetResult();
        }

        Check(fixture.Audit.Capital?.SpendableThb == 3000, "stale read does not reach audit");
        var cached = await fixture.Risk.Size(
            Guid.NewGuid(),
            fixture.Rules,
            fixture.Book("ETH"),
            110m,
            new RiskSignal(99m, fixture.Book("ETH").Bars[^1].Timestamp, fixture.Time.Now, "cache-check"),
            false,
            CancellationToken.None
        );
        Check(
            cached.Capital.SpendableThb == 3000 && fixture.Evidence.ReadCalls == 3,
            "fresh cache survives stale read rejection"
        );
    }

    private static async Task InvalidationWhileAuditPending()
    {
        using var fixture = new AutonomousFixture();
        await fixture.Risk.Read(fixture.Rules, true, CancellationToken.None);
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Audit.BeforeObserve = barrier;
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with { Balances = [new Balance("THB", 2000, 2000)] };
        var old = fixture.Risk.Read(fixture.Rules, true, CancellationToken.None);
        try
        {
            Check(!old.IsCompleted, "audit-pending read remains in flight");
            fixture.Risk.Invalidate();
            barrier.TrySetResult();
            await RejectsInvalidation(old, "audit-pending read is rejected after invalidation");
        }
        finally
        {
            barrier.TrySetResult();
        }

        var rejected = await fixture.Risk.Size(
            Guid.NewGuid(),
            fixture.Rules,
            fixture.Book("ETH"),
            110m,
            new RiskSignal(99m, fixture.Book("ETH").Bars[^1].Timestamp, fixture.Time.Now, "cache-check"),
            false,
            CancellationToken.None
        );
        Check(
            !rejected.Decision.Accepted
                && rejected.Decision.Code == "unknownCapital"
                && rejected.Capital.SpendableThb is null,
            "invalidated audit read leaves cache unknown"
        );
        fixture.Audit.BeforeObserve = null;
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with { Balances = [new Balance("THB", 3000, 3000)] };
        Check(
            (await fixture.Risk.Read(fixture.Rules, false, CancellationToken.None)).Snapshot.SpendableThb == 3000,
            "post-invalidation read recovers"
        );
    }

    private static async Task RejectsInvalidation(Task<CapitalReadResult> task, string name)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException(name);
        }
        catch (InvalidDataException exception)
            when (exception.Message == "Account changed during capital refresh. Request fresh evidence.")
        {
            Console.WriteLine("Passed: " + name);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(name);
        }

        Console.WriteLine("Passed: " + name);
    }
}
