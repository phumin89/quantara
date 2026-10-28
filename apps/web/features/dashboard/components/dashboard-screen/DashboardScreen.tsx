'use client';

import { Component, type ContextType } from 'react';
import NextLink from 'next/link';
import {
  Alert,
  Box,
  Button,
  Chip,
  LinearProgress,
  Stack,
  Typography,
} from '@mui/material';
import RefreshOutlined from '@mui/icons-material/RefreshOutlined';
import { DashboardServiceContext } from '../../context/DashboardServiceContext';
import { DashboardPresentation } from '../../presentation/DashboardPresentation';
import { DashboardFinancials } from '../../presentation/DashboardFinancials';
import { TeamActivitySnapshot } from '../../../team/presentation/TeamActivitySnapshot';
import { OfficePresentation } from '../../../home/presentation/OfficePresentation';
import { DateOnlyPicker } from '../../../../shared/components/date-only-picker/DateOnlyPicker';
import SummaryMetric from '../../../crypto/components/summary-metric/SummaryMetric';
import ProfitLossAppearance from '../../../crypto/presentation/ProfitLossAppearance';
import DashboardChart from './DashboardChart';
import type { DashboardScreenProps } from './DashboardScreenProps';
import type { DashboardScreenState } from './DashboardScreenState';

export default class DashboardScreen extends Component<
  DashboardScreenProps,
  DashboardScreenState
> {
  public static override contextType = DashboardServiceContext.value;

  declare public context: ContextType<typeof DashboardServiceContext.value>;

  public override state: DashboardScreenState = {
    date: '',
    trading: null,
    risk: null,
    loading: true,
    tradingError: null,
    riskError: null,
    checkedAt: null,
  };

  private mounted = false;

  private request: AbortController | null = null;

  private timer: number | null = null;

  public override componentDidMount(): void {
    this.mounted = true;
    window.addEventListener('popstate', this.syncLocation);
    window.addEventListener('quantara:route-change', this.syncLocation);
    this.syncLocation();
    void this.load();
    this.timer = window.setInterval(() => {
      if (!document.hidden && !this.request) {
        void this.load();
      }
    }, 15000);
  }

  public override componentDidUpdate(previous: DashboardScreenProps): void {
    if (
      previous.activity.scopeRevision !== this.props.activity.scopeRevision ||
      previous.activity.accountKey !== this.props.activity.accountKey
    ) {
      this.request?.abort();
      this.setState(
        { trading: null, risk: null, checkedAt: null },
        () => void this.load(),
      );
    } else if (
      previous.activity.date !== this.props.activity.date &&
      this.state.date === previous.activity.date
    ) {
      this.selectDate(this.props.activity.date, true);
    }
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    window.removeEventListener('popstate', this.syncLocation);
    window.removeEventListener('quantara:route-change', this.syncLocation);
    this.request?.abort();
    if (this.timer !== null) {
      window.clearInterval(this.timer);
    }
  }

  public override render() {
    const { date, trading, risk, loading, checkedAt } = this.state;
    const now = this.props.activity.now || Date.now();
    const day = DashboardPresentation.day(trading, date);
    const equity = DashboardFinancials.capital(risk, trading, now);
    const accountReturn = DashboardFinancials.accountReturn(risk, now);
    const displayedAccountKey = trading?.accountKey ?? risk?.accountKey;
    const sourceAt =
      risk?.capital?.observedAt ?? trading?.objective?.observedAt;
    const live = trading?.live;
    const evaluation = trading?.autonomy?.evaluation;
    const firstStudy = evaluation?.metrics.find(
      (metric) => metric.segment === 'validation',
    );
    const strategyPoints =
      evaluation?.metrics
        .filter(
          (metric) =>
            metric.segment === 'validation' &&
            metric.symbol === firstStudy?.symbol &&
            metric.timeframeSeconds === firstStudy.timeframeSeconds,
        )
        .map((metric) => ({ label: metric.strategy, value: metric.netThb })) ??
      [];

    return (
      <Stack
        spacing="var(--section-gap)"
        sx={{ width: '100%', maxWidth: 1440 }}
      >
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          sx={{
            justifyContent: 'space-between',
            gap: 2,
            p: 'var(--panel-padding)',
            bgcolor: 'var(--surface-soft)',
            border: '1.5px solid var(--border-panel)',
            borderRadius: 'var(--radius-panel)',
            boxShadow: 'var(--shadow-panel)',
          }}
        >
          <Box>
            <Typography
              component="h1"
              sx={{ fontSize: { xs: 32, sm: 36 }, fontWeight: 900 }}
            >
              Dashboard
            </Typography>
            <Typography
              variant="body2"
              color="text.secondary"
            >
              Capital and performance in Bangkok time
            </Typography>
          </Box>
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: {
                xs: 'minmax(0, 1fr) 44px',
                sm: '210px 44px',
              },
              alignItems: 'center',
              gap: 1,
              minWidth: 0,
            }}
          >
            <DateOnlyPicker
              label="Performance date"
              value={date}
              maxDate={TeamActivitySnapshot.today(now)}
              onChange={(value) => {
                if (DashboardPresentation.date(value, now)) {
                  this.selectDate(value);
                }
              }}
            />
            <Button
              aria-label="Refresh dashboard"
              variant="outlined"
              disabled={loading}
              onClick={() => void this.load()}
              sx={{ minWidth: 44, minHeight: 44 }}
            >
              <RefreshOutlined />
            </Button>
          </Box>
        </Stack>
        {loading && <LinearProgress aria-label="Loading dashboard records" />}
        {(this.state.tradingError || this.state.riskError) && (
          <Alert severity="warning">
            Some data is unavailable. Retained values show their original
            timestamps; missing metrics stay unavailable.
          </Alert>
        )}
        <Stack
          direction="row"
          sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}
        >
          <Chip
            size="small"
            variant="outlined"
            label={'Current mode: ' + DashboardPresentation.mode(trading)}
          />
          <Typography
            variant="caption"
            color="text.secondary"
          >
            <Box
              component="span"
              sx={{ display: 'block' }}
            >
              Current account valuation {OfficePresentation.time(sourceAt)}.
            </Box>
            <Box
              component="span"
              sx={{ display: 'block' }}
            >
              Selected day affects performance charts.
            </Box>
          </Typography>
          <Typography
            variant="caption"
            color="text.secondary"
            title={displayedAccountKey}
          >
            Financial source account:{' '}
            {displayedAccountKey
              ? `…${displayedAccountKey.slice(-8)}`
              : 'Unavailable'}
          </Typography>
        </Stack>
        <Box
          component="section"
          aria-label="Key performance indicators"
          sx={{
            display: 'grid',
            gridTemplateColumns: {
              xs: 'minmax(0, 1fr)',
              sm: 'repeat(2, minmax(0, 1fr))',
              xl: 'repeat(4, minmax(0, 1fr))',
            },
            bgcolor: 'var(--surface-raised)',
            border: '1.5px solid var(--border-panel)',
            borderRadius: 'var(--radius-panel)',
            boxShadow: 'var(--shadow-panel)',
            p: 'var(--panel-padding)',
            gap: 2,
          }}
        >
          <SummaryMetric
            testId="dashboard-equity"
            label="Account equity"
            value={DashboardPresentation.money(equity)}
            caption={DashboardFinancials.capitalCaption(risk, trading, now)}
          />
          <Box sx={{ color: ProfitLossAppearance.color(day.realizedNet) }}>
            <SummaryMetric
              testId="dashboard-day-net"
              label="Live realized net P/L"
              value={DashboardPresentation.money(day.realizedNet)}
              caption={'Confirmed engine exits: ' + date}
            />
          </Box>
          <SummaryMetric
            testId="dashboard-return"
            label="Account return"
            value={
              accountReturn === null
                ? 'Unavailable'
                : new Intl.NumberFormat('en-US', {
                    style: 'percent',
                    maximumFractionDigits: 2,
                  }).format(accountReturn)
            }
            caption={DashboardFinancials.returnCaption(risk, now)}
          />
          <SummaryMetric
            testId="dashboard-drawdown"
            label="Account drawdown"
            value="Unavailable"
            caption="Account drawdown history is not available."
          />
        </Box>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns:
              'repeat(auto-fit, minmax(min(100%, 20rem), 1fr))',
            gap: 3,
          }}
        >
          <DashboardChart
            title="Live engine equity"
            kind="line"
            points={DashboardFinancials.series(
              trading,
              date,
              'endingEquityThb',
              now,
            )}
            empty="No retained engine equity observations for this period."
            source="7 days of actual retained engine daily marks, with gaps for missing records. This is allocated strategy capital, not account-wide equity history or return."
          />
          <DashboardChart
            title="Live realized net P/L"
            kind="bar"
            points={DashboardFinancials.series(
              trading,
              date,
              'realizedNetThb',
              now,
            )}
            empty="No confirmed live engine outcomes for this period."
            source="7 days of confirmed exits after entry and exit fees. Deposits, withdrawals, manual trades and Paper results are excluded."
          />
        </Box>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: {
              xs: 'minmax(0, 1fr)',
              lg: 'minmax(0, 1.4fr) minmax(0, 1fr)',
            },
            gap: 3,
          }}
        >
          <Box
            component="details"
            sx={{ minWidth: 0 }}
          >
            <Box
              component="summary"
              sx={{
                minHeight: 44,
                alignContent: 'center',
                cursor: 'pointer',
                fontSize: 18,
                fontWeight: 800,
              }}
            >
              Strategy research (hypothetical)
            </Box>
            <DashboardChart
              title="Strategy research (hypothetical)"
              kind="bar"
              points={strategyPoints}
              empty="No retained strategy validation study is available."
              source={
                firstStudy
                  ? `${firstStudy.symbol}, ${String(firstStudy.timeframeSeconds / 60)}m validation. Independent scenarios; do not add them together. Small samples do not establish an edge. Study ${OfficePresentation.time(evaluation?.observedAt)}.`
                  : 'Actual per-strategy profit attribution is not exposed by the current API. Simulation results never represent account profit.'
              }
            />
          </Box>
          <Box
            component="details"
            sx={{ minWidth: 0 }}
          >
            <Box
              component="summary"
              sx={{
                minHeight: 44,
                alignContent: 'center',
                cursor: 'pointer',
                fontSize: 18,
                fontWeight: 800,
              }}
            >
              Performance scope
            </Box>
            <Box
              component="dl"
              sx={{
                m: 0,
                mt: 2,
                display: 'grid',
                gridTemplateColumns: '1fr auto',
                gap: 1.5,
                fontVariantNumeric: 'tabular-nums',
              }}
            >
              <Typography
                component="dt"
                variant="body2"
              >
                Live unrealized P/L
              </Typography>
              <Typography
                component="dd"
                variant="body2"
                sx={{ m: 0 }}
              >
                {DashboardPresentation.money(
                  DashboardFinancials.number(live?.unrealizedThb),
                )}
              </Typography>
              <Typography
                component="dt"
                variant="body2"
              >
                Live execution fees
              </Typography>
              <Typography
                component="dd"
                variant="body2"
                sx={{ m: 0 }}
              >
                {DashboardPresentation.money(
                  DashboardFinancials.number(live?.feesThb),
                )}
              </Typography>
              <Typography
                component="dt"
                variant="body2"
              >
                Paper realized net P/L
              </Typography>
              <Typography
                component="dd"
                variant="body2"
                sx={{ m: 0 }}
              >
                {DashboardPresentation.money(
                  DashboardFinancials.number(trading?.paper.realizedNetThb),
                )}
              </Typography>
            </Box>
            <Typography
              variant="caption"
              color="text.secondary"
              component="p"
              sx={{ mt: 2 }}
            >
              Retained engine totals. Heartbeat{' '}
              {OfficePresentation.time(trading?.heartbeatAt)}. Live and Paper
              stay separate.
            </Typography>
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ mt: 2 }}
            >
              Account equity history and deposit/withdrawal totals are not
              exposed by these read APIs. Company income and account-wide net
              profit remain unavailable.
            </Typography>
            {risk?.performance && (
              <Typography
                variant="caption"
                color="text.secondary"
                component="p"
              >
                Account return interval:{' '}
                {OfficePresentation.time(risk.performance.from)} –{' '}
                {OfficePresentation.time(risk.performance.to)}.
              </Typography>
            )}
            <Button
              component={NextLink}
              href="/crypto#trading"
              sx={{ mt: 1, minHeight: 44 }}
            >
              View holdings and execution records
            </Button>
          </Box>
        </Box>
        <Stack
          direction="row"
          sx={{ justifyContent: 'space-between', gap: 1, flexWrap: 'wrap' }}
        >
          <Button
            component={NextLink}
            href="/daily-brief"
            sx={{ minHeight: 44 }}
          >
            Read research reports
          </Button>
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ alignSelf: 'center' }}
          >
            Checked {OfficePresentation.time(checkedAt)}. Source: {day.source}
          </Typography>
        </Stack>
      </Stack>
    );
  }

  private readonly syncLocation = (): void => {
    const date = new URLSearchParams(window.location.search).get('date') ?? '';
    const now = Date.now();
    const selectedDate = DashboardPresentation.date(date, now)
      ? date
      : TeamActivitySnapshot.today(now);
    if (selectedDate !== this.state.date) {
      this.setState({ date: selectedDate });
    }
  };

  private selectDate(date: string, replace: boolean = false): void {
    const location = new URL(window.location.href);
    if (location.searchParams.get('date') !== date) {
      location.searchParams.set('date', date);
      if (replace) {
        window.history.replaceState(window.history.state, '', location.href);
      } else {
        window.history.pushState(window.history.state, '', location.href);
      }
    }

    if (this.state.date !== date) {
      this.setState({ date });
    }
  }

  private async load(): Promise<void> {
    this.request?.abort();
    const request = new AbortController();
    this.request = request;
    const signal = AbortSignal.any([request.signal, AbortSignal.timeout(8000)]);
    const scope = this.props.activity.scopeRevision;
    this.setState({ loading: true });
    const services = DashboardServiceContext.require(this.context);
    const [trading, risk] = await Promise.allSettled([
      services.trading.get(signal),
      services.risk.get(signal),
    ]);
    if (
      !this.mounted ||
      request.signal.aborted ||
      scope !== this.props.activity.scopeRevision
    ) {
      return;
    }

    this.setState((previous) => {
      let nextTrading =
        trading.status === 'fulfilled' ? trading.value : previous.trading;
      let nextRisk = risk.status === 'fulfilled' ? risk.value : previous.risk;
      const mismatch =
        nextTrading !== null &&
        nextRisk !== null &&
        nextTrading.accountKey !== nextRisk.accountKey;

      let tradingError =
        trading.status === 'rejected' ? 'Trading unavailable' : null;
      let riskError =
        risk.status === 'rejected' ? 'Capital evidence unavailable' : null;
      if (mismatch) {
        nextTrading = null;
        nextRisk = null;
        tradingError = 'Account provenance mismatch';
        riskError = 'Account provenance mismatch';
      }

      return {
        trading: nextTrading,
        risk: nextRisk,
        tradingError,
        riskError,
        loading: false,
        checkedAt: Date.now(),
      };
    });
    this.request = null;
  }
}
