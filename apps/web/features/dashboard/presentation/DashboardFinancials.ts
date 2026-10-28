import type { TradingOverview } from '../../trading/contracts/TradingOverview';
import type { RiskOverview } from '../../crypto/contracts/RiskOverview';
import type { DashboardChartPoint } from './DashboardChartPoint';
import { TeamActivitySnapshot } from '../../team/presentation/TeamActivitySnapshot';

export class DashboardFinancials {
  public static number(
    value: string | number | null | undefined,
  ): number | null {
    return value == null || !Number.isFinite(Number(value))
      ? null
      : Number(value);
  }

  public static capital(
    risk: RiskOverview | null,
    trading: TradingOverview | null,
    now: number,
  ): number | null {
    const capital = risk?.capital;
    if (capital) {
      return !capital.blocker &&
        !capital.hasUncertainOrders &&
        this.fresh(capital.observedAt, now, risk.policy.maximumDataAgeSeconds)
        ? this.number(capital.equityThb)
        : null;
    }

    const objective = trading?.objective;

    return objective?.valuationStatus === 'verified' &&
      this.fresh(objective.observedAt, now, objective.maximumAgeSeconds ?? 90)
      ? this.number(objective.portfolioValueThb)
      : null;
  }

  public static accountReturn(
    risk: RiskOverview | null,
    now: number,
  ): number | null {
    const performance = risk?.performance;
    const capital = risk?.capital;

    return performance &&
      capital &&
      !capital.blocker &&
      !capital.hasUncertainOrders &&
      Date.parse(performance.from) < Date.parse(performance.to) &&
      Date.parse(performance.to) === Date.parse(capital.observedAt) &&
      this.fresh(performance.to, now, risk.policy.maximumDataAgeSeconds)
      ? this.number(performance.timeWeightedReturn)
      : null;
  }

  public static capitalCaption(
    risk: RiskOverview | null,
    trading: TradingOverview | null,
    now: number,
  ): string {
    if (this.capital(risk, trading, now) !== null) {
      return 'Current verified broker valuation';
    }

    const capital = risk?.capital;
    if (capital?.blocker?.trim()) {
      return capital.blocker;
    }

    if (capital?.hasUncertainOrders) {
      return 'An order outcome is unconfirmed. Review orders in Crypto.';
    }

    if (
      capital &&
      !this.fresh(capital.observedAt, now, risk.policy.maximumDataAgeSeconds)
    ) {
      return 'The latest account valuation is out of date.';
    }

    return 'No verified account valuation is available.';
  }

  public static returnCaption(risk: RiskOverview | null, now: number): string {
    if (this.accountReturn(risk, now) !== null) {
      return 'Recorded interval · adjusted for flows';
    }

    const capital = risk?.capital;
    if (!capital || this.capital(risk, null, now) === null) {
      return 'Return needs a current, verified account valuation.';
    }

    const performance = risk.performance;
    if (
      performance &&
      (Date.parse(performance.from) >= Date.parse(performance.to) ||
        Date.parse(performance.to) !== Date.parse(capital.observedAt))
    ) {
      return 'Return is unavailable for the latest valuation.';
    }

    const explanation = performance?.explanation.trim();

    return explanation || 'No complete return interval has been recorded.';
  }

  public static series(
    trading: TradingOverview | null,
    date: string,
    field: 'endingEquityThb' | 'realizedNetThb',
    now: number,
  ): DashboardChartPoint[] {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) {
      return [];
    }

    const end = Date.parse(date + 'T00:00:00Z');
    const liveMode = trading?.mode != null && trading.mode !== 'paper';
    const latest = new Map<
      string,
      NonNullable<TradingOverview>['reports'][number]
    >();
    for (const report of trading?.reports ?? []) {
      if (report.mode !== 'autoLive' || report.date > date) {
        continue;
      }

      const retained = latest.get(report.date);
      if (
        !retained ||
        report.revision > retained.revision ||
        (report.revision === retained.revision &&
          Date.parse(report.createdAt) > Date.parse(retained.createdAt))
      ) {
        latest.set(report.date, report);
      }
    }

    return Array.from({ length: 7 }, (_, index) => {
      const day = new Date(end - (6 - index) * 86400000)
        .toISOString()
        .slice(0, 10);
      let value = this.number(latest.get(day)?.[field]);
      if (liveMode && day === TeamActivitySnapshot.today(now) && day === date) {
        if (
          field === 'realizedNetThb' &&
          trading.autonomy?.funnel?.date === day
        ) {
          value = this.number(trading.autonomy.funnel.netThb);
        }

        if (field === 'endingEquityThb') {
          value = this.number(trading.live.equityThb);
        }
      }

      if (
        day === TeamActivitySnapshot.today(now) &&
        liveMode &&
        !this.fresh(trading.heartbeatAt, now, 180)
      ) {
        value = null;
      }

      return { label: day, value };
    });
  }

  public static fresh(
    value: string | null | undefined,
    now: number,
    seconds: number,
  ): boolean {
    const at = value ? Date.parse(value) : NaN;

    return Number.isFinite(at) && at <= now && now - at <= seconds * 1000;
  }
}
