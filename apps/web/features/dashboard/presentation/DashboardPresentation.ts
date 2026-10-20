import type { TradingOverview } from '../../trading/contracts/TradingOverview';
import type { PortfolioResponse } from '../../crypto/contracts/PortfolioResponse';
import type { TeamActivityValue } from '../../team/context/TeamActivityValue';
import { CompanyLoopStatus } from '../../team/contracts/CompanyLoopStatus';
import { TeamActivitySnapshot } from '../../team/presentation/TeamActivitySnapshot';
import type { DashboardDayResult } from './DashboardDayResult';
import type { DashboardIssue } from './DashboardIssue';
import { DailyBriefView } from '../../team/components/daily-brief-page/DailyBriefView';

export class DashboardPresentation {
  public static researchHref(date: string, runId?: string): string {
    const fragment = runId ? `#run-${encodeURIComponent(runId)}` : '';

    return `/daily-brief?date=${encodeURIComponent(date)}&view=${DailyBriefView.TeamChat}${fragment}`;
  }

  public static researchError(activity: TeamActivityValue): string | null {
    if (activity.error) {
      return activity.error;
    }

    if (
      activity.fetchedAt === null ||
      activity.now - activity.fetchedAt > 30000
    ) {
      return 'Current research evidence is stale or unavailable.';
    }

    return null;
  }

  public static date(value: string, now: number): boolean {
    return (
      /^\d{4}-\d{2}-\d{2}$/.test(value) &&
      Number.isFinite(Date.parse(`${value}T00:00:00Z`)) &&
      new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value &&
      value >= '1970-01-01' &&
      value <= TeamActivitySnapshot.today(now)
    );
  }

  public static money(value: number | null): string {
    return value === null
      ? 'Unavailable'
      : new Intl.NumberFormat('en-US', {
          style: 'currency',
          currency: 'THB',
          currencyDisplay: 'code',
          minimumFractionDigits: 2,
          maximumFractionDigits: 2,
        }).format(value);
  }

  public static day(
    trading: TradingOverview | null,
    date: string,
  ): DashboardDayResult {
    const funnel =
      trading?.mode !== 'paper' && trading?.autonomy?.funnel?.date === date
        ? trading.autonomy.funnel
        : null;
    const report = trading?.reports
      .filter((item) => item.mode === 'autoLive' && item.date === date)
      .sort(
        (left, right) =>
          right.revision - left.revision ||
          Date.parse(right.createdAt) - Date.parse(left.createdAt),
      )[0];

    let source = 'No confirmed engine result is available for this day.';
    if (funnel) {
      source = 'Current-day engine ledger';
    } else if (report) {
      source = `Retained daily report · revision ${String(report.revision)}`;
    }

    return {
      realizedNet: funnel
        ? this.number(funnel.netThb)
        : this.number(report?.realizedNetThb),
      filledOrders: funnel?.filledOrders ?? null,
      closedTrades: funnel?.closedTrades ?? null,
      recordedAt: funnel
        ? (trading?.autonomy?.observedAt ?? null)
        : (report?.createdAt ?? null),
      source,
    };
  }

  public static mode(trading: TradingOverview | null): string {
    switch (trading?.mode) {
      case 'paper':
        return 'Paper simulation';
      case 'autoLive':
        return 'AutoLive';
      case 'manualReview':
        return 'Manual review';
      case 'stopped':
        return 'Stopped';
      case undefined:
        return 'Unavailable';
    }
  }

  public static issues(
    portfolio: PortfolioResponse | null,
    trading: TradingOverview | null,
    activity: TeamActivityValue,
  ): DashboardIssue[] {
    const issues: DashboardIssue[] = [];
    if (portfolio?.error) {
      issues.push({
        key: 'wallet-error',
        severity: 'error',
        title: 'Wallet refresh needs attention',
        href: '/crypto',
        action: 'Review wallet',
      });
    }

    if (
      portfolio &&
      (!portfolio.updatedAt ||
        this.stale(portfolio.updatedAt, activity.now, 300))
    ) {
      issues.push({
        key: 'wallet-stale',
        severity: 'warning',
        title: 'Wallet evidence is unavailable or older than 5 minutes',
        href: '/crypto',
        action: 'Review sync',
      });
    }

    if (
      portfolio?.summary.totalAssets === null ||
      (portfolio?.performance?.unreconciledSymbols.length ?? 0) > 0
    ) {
      issues.push({
        key: 'wallet-coverage',
        severity: 'warning',
        title: 'Some portfolio evidence is incomplete',
        href: '/crypto',
        action: 'Review coverage',
      });
    }

    if (trading) {
      if (trading.emergencyStopped || trading.fault) {
        issues.push({
          key: 'trading-fault',
          severity: 'error',
          title: trading.emergencyStopped
            ? 'Emergency stop is active'
            : 'Trading needs attention',
          href: '/daily-brief',
          action: 'Review trading',
        });
      }

      const uncertain = trading.live.orders.filter((order) =>
        ['submitting', 'unknown'].includes(order.status),
      ).length;
      if (uncertain > 0) {
        issues.push({
          key: 'uncertain-orders',
          severity: 'error',
          title: `${String(uncertain)} live order outcome(s) need reconciliation`,
          href: '/daily-brief',
          action: 'Review orders',
        });
      }

      if (
        !trading.heartbeatAt ||
        this.stale(trading.heartbeatAt, activity.now, 180)
      ) {
        issues.push({
          key: 'engine-stale',
          severity: 'warning',
          title: 'Trading heartbeat is unavailable or older than 3 minutes',
          href: '/daily-brief',
          action: 'Check trading status',
        });
      }

      const mandate = trading.mandate;
      if (
        trading.mode === 'autoLive' &&
        (!mandate ||
          mandate.accountKey !== trading.accountKey ||
          mandate.rulesHash !== trading.rulesHash ||
          mandate.revokedAt !== null ||
          Date.parse(mandate.expiresAt) <= activity.now)
      ) {
        issues.push({
          key: 'mandate',
          severity: 'error',
          title: 'Live authority needs review',
          href: '/daily-brief',
          action: 'Review authority',
        });
      }

      if ((trading.notifications?.unknown ?? 0) > 0) {
        issues.push({
          key: 'notification',
          severity: 'warning',
          title: 'Notification delivery is uncertain',
          href: '/daily-brief',
          action: 'Review delivery',
        });
      }
    }

    const company = activity.overview?.company;
    if (
      activity.error ||
      activity.fetchedAt === null ||
      activity.now - activity.fetchedAt > 30000
    ) {
      issues.push({
        key: 'team-stale',
        severity: 'warning',
        title: 'Current team status is unavailable or stale',
        href: '/',
        action: 'View research office',
      });
    }

    if (
      activity.overview?.configured === false ||
      company?.status === CompanyLoopStatus.WaitingForCodex
    ) {
      issues.push({
        key: 'team-auth',
        severity: 'warning',
        title: 'Research is waiting for Codex',
        href: '/daily-brief',
        action: 'Review team setup',
      });
    } else if (
      company?.error ||
      company?.status === CompanyLoopStatus.Error ||
      company?.status === CompanyLoopStatus.WaitingForPortfolio
    ) {
      issues.push({
        key: 'team-error',
        severity: 'error',
        title: 'Research needs attention',
        href: '/daily-brief',
        action: 'Review research',
      });
    }

    return issues;
  }

  private static number(value: string | null | undefined): number | null {
    return value == null ? null : Number(value);
  }

  private static stale(value: string, now: number, seconds: number): boolean {
    const at = Date.parse(value);

    return !Number.isFinite(at) || at > now + 5000 || now - at > seconds * 1000;
  }
}
