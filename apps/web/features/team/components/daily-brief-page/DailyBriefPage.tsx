'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  Box,
  Button,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material';
import { TeamServiceContext } from '../../context/TeamServiceContext';
import { TeamRunStatus } from '../../contracts/TeamRunStatus';
import { CompanyLoopStatus } from '../../contracts/CompanyLoopStatus';
import type { DailyBriefPageState } from './DailyBriefPageState';
import OrderReviewPanel from '../../../orders/components/order-review-panel/OrderReviewPanel';
import DailyBriefNews from '../daily-brief-news/DailyBriefNews';
import DailyBriefConversation from '../daily-brief-conversation/DailyBriefConversation';
import DailyBriefSummary from '../daily-brief-summary/DailyBriefSummary';
import TeamResearchStatus from '../team-research-status/TeamResearchStatus';
import TeamMembers from '../team-members/TeamMembers';
import TradingPanel from '../../../trading/components/trading-panel/TradingPanel';
import { DailyBriefView } from './DailyBriefView';

export default class DailyBriefPage extends Component<
  object,
  DailyBriefPageState
> {
  public static override readonly contextType = TeamServiceContext.value;

  public override context: ContextType<typeof TeamServiceContext.value> = null;

  public override state: DailyBriefPageState = {
    date: '',
    brief: null,
    overview: null,
    loading: true,
    error: null,
    membersOpen: false,
    controlling: false,
    view: DailyBriefView.Trading,
    news: null,
    newsError: null,
  };

  private mounted: boolean = false;

  private generation: number = 0;

  private request: AbortController | null = null;

  private timer: number | null = null;

  private action: AbortController | null = null;

  public override componentDidMount(): void {
    this.mounted = true;
    const query = new URLSearchParams(window.location.search);
    const date = query.get('date') ?? '';
    const validDate =
      /^\d{4}-\d{2}-\d{2}$/.test(date) &&
      !Number.isNaN(Date.parse(`${date}T00:00:00Z`)) &&
      new Date(`${date}T00:00:00Z`).toISOString().slice(0, 10) === date &&
      date >= '1970-01-01' &&
      date <= DailyBriefPage.today();
    this.setState(
      {
        date: validDate ? date : DailyBriefPage.today(),
        view:
          query.get('view') === DailyBriefView.TeamChat
            ? DailyBriefView.TeamChat
            : DailyBriefView.Trading,
      },
      () => void this.load(),
    );
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.request?.abort();
    this.action?.abort();
    this.clearTimer();
  }

  public override render(): ReactNode {
    const {
      date,
      brief,
      loading,
      error,
      membersOpen,
      controlling,
      overview,
      view,
    } = this.state;
    const { news, newsError } = this.state;

    return (
      <Stack
        spacing={2}
        sx={{
          width: '100%',
          maxWidth: 1280,
          '& summary': {
            minHeight: 44,
            alignContent: 'center',
            '&:focus-visible': {
              outline: '2px solid #1e40af',
              outlineOffset: 2,
            },
          },
        }}
      >
        <Box
          sx={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 2,
          }}
        >
          <Box>
            <Typography
              component="h1"
              sx={{ fontSize: 26, fontWeight: 700 }}
            >
              Daily Brief
            </Typography>
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ display: { xs: 'none', sm: 'block' } }}
            >
              Trading at a glance. Your team, one tab away.
            </Typography>
          </Box>
          <TextField
            type="date"
            label="Date"
            helperText="Report date · Bangkok"
            value={date}
            slotProps={{
              inputLabel: { shrink: true },
              htmlInput: { max: DailyBriefPage.today(), min: '1970-01-01' },
            }}
            onChange={(event) => {
              this.selectDate(event.target.value);
            }}
            sx={{ width: { xs: 170, sm: 190 } }}
          />
        </Box>
        {error && (
          <Alert
            severity="error"
            action={
              <Button
                color="inherit"
                onClick={() => void this.load()}
              >
                Retry
              </Button>
            }
          >
            {error}
          </Alert>
        )}
        <Tabs
          value={view}
          onChange={(_, nextView: DailyBriefView) => {
            this.setState({ view: nextView });
          }}
          aria-label="Daily brief sections"
          sx={{ minHeight: 44, borderBottom: '1px solid #e0e0e0' }}
        >
          <Tab
            id="brief-trading-tab"
            aria-controls="brief-trading-panel"
            value={DailyBriefView.Trading}
            label="Trading status"
          />
          <Tab
            id="brief-team-tab"
            aria-controls="brief-team-panel"
            value={DailyBriefView.TeamChat}
            label="Team chat"
          />
        </Tabs>
        <Box
          role="tabpanel"
          id="brief-trading-panel"
          aria-labelledby="brief-trading-tab"
          hidden={view !== DailyBriefView.Trading}
          aria-label="Trading status"
        >
          <Stack
            spacing={2}
            sx={{ minWidth: 0 }}
          >
            <TradingPanel date={date} />
            <DailyBriefNews
              news={news}
              error={newsError}
            />
            <OrderReviewPanel />
          </Stack>
        </Box>
        <Box
          role="tabpanel"
          id="brief-team-panel"
          aria-labelledby="brief-team-tab"
          hidden={view !== DailyBriefView.TeamChat}
          aria-label="Team chat"
        >
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: {
                xs: 'minmax(0, 1fr)',
                lg: 'minmax(0, 1fr) 260px',
              },
              alignItems: 'start',
              gap: 2,
            }}
          >
            <Stack
              spacing={2}
              sx={{ minWidth: 0 }}
            >
              <TeamResearchStatus
                company={overview?.company}
                loading={loading}
                controlling={controlling}
                onControl={() => void this.controlCompany()}
              />
              <DailyBriefConversation
                active={view === DailyBriefView.TeamChat}
                brief={brief}
                loading={loading}
                error={error}
                date={date}
              />
              <DailyBriefSummary
                brief={brief}
                error={error}
              />
            </Stack>
            <TeamMembers
              latest={brief?.runs.at(-1)}
              overview={overview}
              loading={loading}
              membersOpen={membersOpen}
              onToggle={() => {
                this.setState({ membersOpen: !membersOpen });
              }}
            />
          </Box>
        </Box>
      </Stack>
    );
  }

  private selectDate(date: string): void {
    this.request?.abort();
    this.clearTimer();
    this.generation += 1;
    this.setState(
      { date, brief: null, error: null, loading: Boolean(date) },
      () => {
        if (date) {
          void this.load();
        }
      },
    );
  }

  private async load(preserveError: boolean = false): Promise<void> {
    if (!this.mounted || !this.state.date || this.action) {
      return;
    }

    this.clearTimer();
    this.request?.abort();
    const request = new AbortController();
    this.request = request;
    const generation = ++this.generation;
    const date = this.state.date;
    this.setState({
      loading: true,
      error: preserveError ? this.state.error : null,
    });
    try {
      const service = TeamServiceContext.require(this.context);
      const [brief, overview, newsResult] = await Promise.all([
        service.brief(date, request.signal),
        service.get(request.signal),
        service.news(request.signal).then(
          (news) => ({ news, error: null as string | null }),
          (cause: unknown) => ({
            news: null,
            error:
              cause instanceof Error ? cause.message : 'Source unavailable.',
          }),
        ),
      ]);
      if (this.isCurrent(generation)) {
        this.setState({
          brief,
          overview,
          news: newsResult.news,
          newsError: newsResult.error,
        });
      }
    } catch (cause) {
      if (this.isCurrent(generation) && !request.signal.aborted) {
        this.setState({
          error:
            cause instanceof Error
              ? cause.message
              : 'Unable to load the daily brief.',
        });
      }
    } finally {
      if (this.isCurrent(generation)) {
        this.setState({ loading: false }, () => {
          const status = this.state.overview?.latestRun?.status;
          if (
            this.mounted &&
            generation === this.generation &&
            date === DailyBriefPage.today() &&
            (!this.state.overview ||
              Boolean(this.state.error) ||
              this.state.overview.company?.enabled ||
              status === TeamRunStatus.Queued ||
              status === TeamRunStatus.Running)
          ) {
            const interval =
              status === TeamRunStatus.Queued ||
              status === TeamRunStatus.Running
                ? 5000
                : 30000;
            this.timer = window.setTimeout(() => void this.load(), interval);
          }
        });
      }
    }
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      window.clearTimeout(this.timer);
      this.timer = null;
    }
  }

  private async controlCompany(): Promise<void> {
    const company = this.state.overview?.company;
    if (!company || this.action) {
      return;
    }

    this.clearTimer();
    this.request?.abort();
    this.generation += 1;
    const action = new AbortController();
    this.action = action;
    this.setState({ controlling: true, error: null });
    try {
      await TeamServiceContext.require(this.context).setCompanyEnabled(
        company.status === CompanyLoopStatus.Error ? false : !company.enabled,
        action.signal,
      );
    } catch (cause) {
      if (this.mounted && !action.signal.aborted) {
        this.setState({
          error:
            cause instanceof Error
              ? cause.message
              : 'Unable to update company operation.',
        });
      }
    } finally {
      this.action = null;
      if (this.mounted) {
        this.setState({ controlling: false }, () => void this.load(true));
      }
    }
  }

  private isCurrent(generation: number): boolean {
    return this.mounted && generation === this.generation;
  }

  private static today(): string {
    const parts = new Intl.DateTimeFormat('en', {
      timeZone: 'Asia/Bangkok',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      numberingSystem: 'latn',
    }).formatToParts(new Date());
    const year = parts.find((part) => part.type === 'year');
    const month = parts.find((part) => part.type === 'month');
    const day = parts.find((part) => part.type === 'day');
    if (!year || !month || !day) {
      throw new Error('Unable to determine the current date.');
    }

    return `${year.value}-${month.value}-${day.value}`;
  }
}
