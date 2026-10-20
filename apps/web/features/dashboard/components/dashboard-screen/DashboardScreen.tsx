'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import NextLink from 'next/link';
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Chip,
  Divider,
  LinearProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import RefreshOutlined from '@mui/icons-material/RefreshOutlined';
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined';
import PortfolioSummary from '../../../crypto/components/portfolio-summary/PortfolioSummary';
import SummaryMetric from '../../../crypto/components/summary-metric/SummaryMetric';
import { TeamActivitySnapshot } from '../../../team/presentation/TeamActivitySnapshot';
import { OfficePresentation } from '../../../home/presentation/OfficePresentation';
import { TeamRole } from '../../../team/contracts/TeamRole';
import { TeamRunStatus } from '../../../team/contracts/TeamRunStatus';
import { DashboardPresentation } from '../../presentation/DashboardPresentation';
import type { DashboardScreenProps } from './DashboardScreenProps';
import type { DashboardScreenState } from './DashboardScreenState';
import { DateOnlyPicker } from '../../../../shared/components/date-only-picker/DateOnlyPicker';
import { DashboardServiceContext } from '../../context/DashboardServiceContext';
import ProfitLossAppearance from '../../../crypto/presentation/ProfitLossAppearance';

export default class DashboardScreen extends Component<
  DashboardScreenProps,
  DashboardScreenState
> {
  public static override readonly contextType = DashboardServiceContext.value;

  declare public context: ContextType<typeof DashboardServiceContext.value>;

  public override state: DashboardScreenState = {
    date: '',
    portfolio: null,
    trading: null,
    brief: null,
    loading: true,
    briefLoading: true,
    portfolioError: null,
    tradingError: null,
    briefError: null,
    checkedAt: null,
  };

  private mounted = false;

  private request: AbortController | null = null;

  private briefRequest: AbortController | null = null;

  private timer: number | null = null;

  public override componentDidMount(): void {
    this.mounted = true;
    const now = Date.now();
    const queryDate =
      new URLSearchParams(window.location.search).get('date') ?? '';
    const date = DashboardPresentation.date(queryDate, now)
      ? queryDate
      : TeamActivitySnapshot.today(now);
    this.setState({ date }, () => {
      void this.load();
      void this.loadBrief();
    });
    this.timer = window.setInterval(() => {
      if (!document.hidden && !this.request) {
        void this.load();
      }
    }, 15000);
  }

  public override componentDidUpdate(previous: DashboardScreenProps): void {
    if (previous.activity.scopeRevision !== this.props.activity.scopeRevision) {
      this.request?.abort();
      this.briefRequest?.abort();
      this.setState(
        { portfolio: null, trading: null, brief: null, checkedAt: null },
        () => {
          void this.load();
          void this.loadBrief();
        },
      );
    } else if (
      previous.activity.date !== this.props.activity.date &&
      this.state.date === previous.activity.date
    ) {
      this.setState({ date: this.props.activity.date, brief: null }, () => {
        void this.loadBrief();
      });
    } else if (
      previous.activity.fetchedAt !== this.props.activity.fetchedAt &&
      this.state.date === this.props.activity.date
    ) {
      void this.loadBrief();
    }
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.request?.abort();
    this.briefRequest?.abort();
    if (this.timer !== null) {
      window.clearInterval(this.timer);
    }
  }

  public override render(): ReactNode {
    const { date, portfolio, trading, loading, checkedAt } = this.state;
    const issues = DashboardPresentation.issues(
      portfolio,
      trading,
      this.props.activity,
    );

    return (
      <Stack
        spacing={3}
        sx={{ width: '100%', maxWidth: 1280 }}
      >
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          sx={{ justifyContent: 'space-between', gap: 2 }}
        >
          <Box>
            <Typography
              component="h1"
              sx={{ fontSize: 32, fontWeight: 650, lineHeight: 1.15 }}
            >
              Dashboard
            </Typography>
          </Box>
          <Button
            variant="outlined"
            startIcon={<RefreshOutlined />}
            disabled={loading}
            onClick={this.refresh}
            sx={{ alignSelf: 'flex-start', minHeight: 44 }}
          >
            {loading ? 'Checking records…' : 'Refresh summary'}
          </Button>
        </Stack>
        {loading && <LinearProgress aria-label="Loading dashboard records" />}
        <Box
          component="section"
          aria-labelledby="dashboard-wallet-heading"
        >
          <Stack
            direction="row"
            sx={{
              justifyContent: 'space-between',
              alignItems: 'center',
              gap: 1,
              mb: 1.5,
            }}
          >
            <Typography
              id="dashboard-wallet-heading"
              component="h2"
              sx={{ fontSize: 18, fontWeight: 650 }}
            >
              Current wallet
            </Typography>
            <Button
              component={NextLink}
              href="/crypto"
              endIcon={<ArrowForwardOutlined />}
              sx={{ minHeight: 44 }}
            >
              Open Crypto
            </Button>
          </Stack>
          {this.state.portfolioError && (
            <Alert
              severity="error"
              sx={{ mb: 1.5 }}
            >
              {this.state.portfolioError}{' '}
              {portfolio
                ? 'Showing the last loaded snapshot.'
                : 'Wallet amounts are unavailable.'}
            </Alert>
          )}
          {portfolio ? (
            <PortfolioSummary data={portfolio} />
          ) : (
            <Paper
              variant="outlined"
              sx={{ p: 3 }}
            >
              <Typography color="text.secondary">
                {loading
                  ? 'Loading the saved wallet snapshot…'
                  : 'No verified wallet summary is available. Retry or open Crypto.'}
              </Typography>
            </Paper>
          )}
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: 'block', mt: 1 }}
          >
            Wallet as of {OfficePresentation.time(portfolio?.updatedAt)} ·
            Prices as of {OfficePresentation.time(portfolio?.priceUpdatedAt)}.
            Current balances do not change with the selected day.
          </Typography>
        </Box>
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          sx={{
            alignItems: { sm: 'center' },
            justifyContent: 'space-between',
            gap: 2,
          }}
        >
          <Box>
            <Typography
              component="h2"
              sx={{ fontSize: 20, fontWeight: 650 }}
            >
              Day summary
            </Typography>
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ mt: 0.5 }}
            >
              Bangkok time · 00:00–23:59 ICT
            </Typography>
          </Box>
          <DateOnlyPicker
            label="Summary date"
            value={date}
            onChange={(value) => {
              this.changeDate(value);
            }}
            maxDate={TeamActivitySnapshot.today(
              this.props.activity.now || Date.now(),
            )}
          />
        </Stack>
        {this.state.tradingError && (
          <Alert severity="error">
            {this.state.tradingError}{' '}
            {trading
              ? 'Showing retained engine records.'
              : 'Engine results are unavailable.'}
          </Alert>
        )}
        {this.renderDay()}
        {this.renderTeam()}
        <Box
          component="section"
          aria-labelledby="dashboard-attention-heading"
        >
          <Typography
            id="dashboard-attention-heading"
            component="h2"
            sx={{ fontSize: 18, fontWeight: 650, mb: 1.5 }}
          >
            Current attention
          </Typography>
          <Stack spacing={1}>
            {issues.map((issue) => (
              <Alert
                key={issue.key}
                severity={issue.severity}
              >
                <AlertTitle>{issue.title}</AlertTitle>
                <Button
                  component={NextLink}
                  href={issue.href}
                  size="small"
                  sx={{ minHeight: 44, mt: 0.5 }}
                >
                  {issue.action}
                </Button>
              </Alert>
            ))}
            {issues.length === 0 && (
              <Typography color="text.secondary">
                No additional issues in the loaded records.
              </Typography>
            )}
          </Stack>
        </Box>
        <Typography
          variant="caption"
          color="text.secondary"
        >
          Summary checked {OfficePresentation.time(checkedAt)}.
        </Typography>
      </Stack>
    );
  }

  private renderDay(): ReactNode {
    const result = DashboardPresentation.day(
      this.state.trading,
      this.state.date,
    );

    return (
      <Paper
        component="section"
        aria-label="Confirmed engine day results"
        variant="outlined"
        sx={{ p: { xs: 2, sm: 3 } }}
      >
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          sx={{
            justifyContent: 'space-between',
            alignItems: { sm: 'center' },
            gap: 1,
          }}
        >
          <Typography
            component="h3"
            sx={{ fontSize: 16, fontWeight: 650 }}
          >
            Confirmed engine fills
          </Typography>
          <Chip
            size="small"
            variant="outlined"
            label={`Current mode: ${DashboardPresentation.mode(this.state.trading)}`}
          />
        </Stack>
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ mt: 1 }}
        >
          Real engine fills · net of fees · account-wide P/L unavailable.
        </Typography>
        <Box
          component="details"
          sx={{ mt: 0.5 }}
        >
          <Typography
            component="summary"
            variant="body2"
            sx={{ cursor: 'pointer', minHeight: 44, alignContent: 'center' }}
          >
            Accounting scope
          </Typography>
          <Typography
            variant="body2"
            color="text.secondary"
            sx={{ maxWidth: '75ch' }}
          >
            Paper simulation and manual trades are excluded. Realized P/L
            includes entry and exit net fees. Daily counts require a
            matching-day engine ledger; missing counts stay unavailable.
          </Typography>
        </Box>
        <Stack
          direction={{ xs: 'column', lg: 'row' }}
          spacing={2}
          divider={
            <Divider
              flexItem
              sx={{
                borderBottomWidth: { xs: 1, lg: 0 },
                borderRightWidth: { xs: 0, lg: 1 },
              }}
            />
          }
          sx={{ my: 2.5 }}
        >
          <Box
            sx={{
              flex: 1,
              minWidth: 0,
              color: ProfitLossAppearance.color(result.realizedNet),
            }}
          >
            <SummaryMetric
              testId="dashboard-day-net"
              label="Realized net P/L"
              value={DashboardPresentation.money(result.realizedNet)}
              caption="Confirmed exits after fees"
            />
          </Box>
          <SummaryMetric
            testId="dashboard-day-filled"
            label="Orders with fills"
            value={
              result.filledOrders === null
                ? 'Unavailable'
                : String(result.filledOrders)
            }
            caption="Retained engine evidence"
          />
          <SummaryMetric
            testId="dashboard-day-closed"
            label="Completed sell orders"
            value={
              result.closedTrades === null
                ? 'Unavailable'
                : String(result.closedTrades)
            }
            caption="Confirmed engine closes"
          />
        </Stack>
        <Typography
          variant="body2"
          color="text.secondary"
        >
          {result.source}
        </Typography>
        {result.recordedAt && (
          <Typography
            variant="caption"
            color="text.secondary"
          >
            Recorded {OfficePresentation.time(result.recordedAt)}.
          </Typography>
        )}
      </Paper>
    );
  }

  private renderTeam(): ReactNode {
    const { brief, briefLoading, date } = this.state;
    const briefError =
      this.state.briefError ??
      (date === this.props.activity.date
        ? DashboardPresentation.researchError(this.props.activity)
        : null);
    const runs = brief?.runs ?? [];
    const completed = runs.filter(
      (run) => run.status === TeamRunStatus.Completed,
    ).length;
    const failed = runs.filter(
      (run) => run.status === TeamRunStatus.Failed,
    ).length;
    const reviewed = runs
      .filter((run) =>
        run.steps.some(
          (step) =>
            step.role === TeamRole.Reviewer &&
            step.status === TeamRunStatus.Completed &&
            step.output,
        ),
      )
      .sort(
        (left, right) =>
          Date.parse(right.updatedAt) - Date.parse(left.updatedAt),
      )[0];
    const output = reviewed?.steps.find(
      (step) => step.role === TeamRole.Reviewer,
    )?.output;
    const href = DashboardPresentation.researchHref(date, reviewed?.id);
    let countLabel = 'Report counts unavailable.';
    if (briefLoading) {
      countLabel = 'Loading selected-day reports…';
    } else if (brief && !briefError) {
      countLabel = `${String(runs.length)} retained report(s) · ${String(completed)} completed · ${String(failed)} failed`;
    }

    return (
      <Paper
        component="section"
        aria-labelledby="dashboard-team-heading"
        variant="outlined"
        sx={{ p: { xs: 2, sm: 3 } }}
      >
        <Stack
          direction="row"
          sx={{ justifyContent: 'space-between', alignItems: 'center', gap: 1 }}
        >
          <Typography
            id="dashboard-team-heading"
            component="h3"
            sx={{ fontSize: 16, fontWeight: 650 }}
          >
            Research summary
          </Typography>
          <Button
            component={NextLink}
            href={href}
            endIcon={<ArrowForwardOutlined />}
            sx={{ minHeight: 44 }}
          >
            Read reports
          </Button>
        </Stack>
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ mt: 1 }}
        >
          {OfficePresentation.company(this.props.activity.overview?.company)}
        </Typography>
        {briefError && (
          <Alert
            severity="warning"
            sx={{ mt: 2 }}
          >
            {briefError}
          </Alert>
        )}
        <Typography sx={{ my: 2, fontVariantNumeric: 'tabular-nums' }}>
          {countLabel}
        </Typography>
        {output ? (
          <Box>
            <Typography
              variant="caption"
              color="text.secondary"
            >
              {briefError
                ? 'Saved Reviewer excerpt'
                : 'Latest completed Reviewer excerpt'}{' '}
              · {OfficePresentation.time(reviewed.updatedAt)}
            </Typography>
            <Typography
              sx={{
                mt: 1,
                whiteSpace: 'pre-wrap',
                overflowWrap: 'anywhere',
                maxWidth: '75ch',
              }}
            >
              {output.slice(0, 360)}
              {output.length > 360 ? '…' : ''}
            </Typography>
          </Box>
        ) : (
          <Typography
            variant="body2"
            color="text.secondary"
          >
            {briefError
              ? 'The selected-day summary is unavailable.'
              : 'No completed Reviewer summary is recorded for this day.'}
          </Typography>
        )}
      </Paper>
    );
  }

  private readonly refresh = (): void => {
    void this.load();
    void this.loadBrief();
    void this.props.activity.refresh();
  };

  private changeDate(date: string): void {
    if (
      !DashboardPresentation.date(date, this.props.activity.now || Date.now())
    ) {
      return;
    }

    this.briefRequest?.abort();
    this.setState({ date, brief: null, briefError: null }, () => {
      void this.loadBrief();
    });
  }

  private async load(): Promise<void> {
    this.request?.abort();
    const request = new AbortController();
    this.request = request;
    const signal = AbortSignal.any([request.signal, AbortSignal.timeout(8000)]);
    const scope = this.props.activity.scopeRevision;
    this.setState({ loading: true });
    const [portfolio, trading] = await Promise.allSettled([
      DashboardServiceContext.require(this.context).portfolio.get(signal),
      DashboardServiceContext.require(this.context).trading.get(signal),
    ]);
    if (
      !this.mounted ||
      request.signal.aborted ||
      scope !== this.props.activity.scopeRevision
    ) {
      return;
    }

    const key = this.props.activity.accountKey;
    const mismatch =
      trading.status === 'fulfilled' &&
      key !== null &&
      trading.value.accountKey !== key;
    let tradingError =
      trading.status === 'rejected'
        ? 'Unable to read engine records. Retry or open Daily Brief.'
        : null;
    if (mismatch) {
      tradingError =
        'Trading and team account evidence do not match. Reload before using the summary.';
    }

    this.setState((previous) => {
      let nextPortfolio =
        portfolio.status === 'fulfilled' ? portfolio.value : previous.portfolio;
      let nextTrading =
        trading.status === 'fulfilled' ? trading.value : previous.trading;
      if (mismatch) {
        nextPortfolio = null;
        nextTrading = null;
      }

      return {
        portfolio: nextPortfolio,
        trading: nextTrading,
        portfolioError:
          portfolio.status === 'rejected'
            ? 'Unable to read the saved wallet. Retry or open Crypto.'
            : null,
        tradingError,
        loading: false,
        checkedAt: Date.now(),
      };
    });
    this.request = null;
  }

  private async loadBrief(): Promise<void> {
    this.briefRequest?.abort();
    const request = new AbortController();
    this.briefRequest = request;
    const signal = AbortSignal.any([request.signal, AbortSignal.timeout(8000)]);
    const { activity } = this.props;
    const { date } = this.state;
    const scope = activity.scopeRevision;
    if (!date) {
      return;
    }

    this.setState({ briefLoading: true });
    try {
      const reused = activity.brief?.date === date;
      const brief = reused
        ? activity.brief
        : await activity.service.brief(date, signal);
      if (
        this.mounted &&
        !request.signal.aborted &&
        this.state.date === date &&
        scope === this.props.activity.scopeRevision
      ) {
        const accountKey = this.props.activity.accountKey;
        let briefError =
          accountKey === null
            ? 'Account provenance is unavailable; research records are not shown.'
            : null;
        if (reused && accountKey !== null) {
          briefError = DashboardPresentation.researchError(this.props.activity);
        }

        this.setState({
          brief: {
            date,
            runs:
              accountKey === null
                ? []
                : brief.runs.filter((run) => run.accountKey === accountKey),
          },
          briefLoading: false,
          briefError,
        });
      }
    } catch {
      if (
        this.mounted &&
        !request.signal.aborted &&
        this.state.date === date &&
        scope === this.props.activity.scopeRevision
      ) {
        this.setState({
          brief: null,
          briefLoading: false,
          briefError:
            'Unable to read reports for this day. Retry or open Daily Brief.',
        });
      }
    }
  }
}
