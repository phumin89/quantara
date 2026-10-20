'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  Box,
  Button,
  Stack,
  Tab,
  Tabs,
  Typography,
} from '@mui/material';
import { TeamActivityContext } from '../../context/TeamActivityContext';
import { TeamActivitySnapshot } from '../../presentation/TeamActivitySnapshot';
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
import { DateOnlyPicker } from '../../../../shared/components/date-only-picker/DateOnlyPicker';

export default class DailyBriefPage extends Component<
  object,
  DailyBriefPageState
> {
  public static override readonly contextType = TeamActivityContext.value;

  public override context: ContextType<typeof TeamActivityContext.value> = null;

  public override state: DailyBriefPageState = {
    date: '',
    brief: null,
    scopeRevision: -1,
    newsScopeRevision: -1,
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

  private newsRequest: AbortController | null = null;

  private observedScopeRevision = -1;

  private observedFetchedAt: number | null = null;

  private observedDate = '';

  private action: AbortController | null = null;

  public override componentDidMount(): void {
    this.mounted = true;
    const activity = TeamActivityContext.require(this.context);
    this.observedScopeRevision = activity.scopeRevision;
    this.observedFetchedAt = activity.fetchedAt;
    this.observedDate = activity.date;
    const query = new URLSearchParams(window.location.search);
    const date = query.get('date') ?? '';
    const validDate =
      /^\d{4}-\d{2}-\d{2}$/.test(date) &&
      !Number.isNaN(Date.parse(`${date}T00:00:00Z`)) &&
      new Date(`${date}T00:00:00Z`).toISOString().slice(0, 10) === date &&
      date >= '1970-01-01' &&
      date <= TeamActivitySnapshot.today();
    this.setState(
      {
        date: validDate ? date : TeamActivitySnapshot.today(),
        view:
          query.get('view') === DailyBriefView.TeamChat
            ? DailyBriefView.TeamChat
            : DailyBriefView.Trading,
      },
      () => void this.load(),
    );
  }

  public override componentDidUpdate(): void {
    const activity = TeamActivityContext.require(this.context);
    const scopeChanged = this.observedScopeRevision !== activity.scopeRevision;
    const dayChanged = this.observedDate !== activity.date;
    this.observedScopeRevision = activity.scopeRevision;
    this.observedDate = activity.date;
    if (scopeChanged || dayChanged) {
      this.request?.abort();
      this.newsRequest?.abort();
      this.newsRequest = null;
      this.generation += 1;
      this.setState(
        { brief: null, news: null, error: null, loading: true },
        () => void this.load(),
      );

      return;
    }

    if (this.observedFetchedAt !== activity.fetchedAt) {
      this.observedFetchedAt = activity.fetchedAt;
      if (this.state.date === activity.date) {
        void this.loadNews();
      }
    }
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.request?.abort();
    this.action?.abort();
    this.newsRequest?.abort();
  }

  public override render(): ReactNode {
    const { date, membersOpen, controlling, view } = this.state;
    const activity = TeamActivityContext.require(this.context);
    const current = date === activity.date;
    const historicalBrief =
      this.state.scopeRevision === activity.scopeRevision
        ? this.state.brief
        : null;
    const brief = current ? activity.brief : historicalBrief;
    const loading = current ? activity.loading : this.state.loading;
    const error = this.state.error ?? (current ? activity.error : null);
    const overview = activity.overview;
    const news =
      this.state.newsScopeRevision === activity.scopeRevision
        ? this.state.news
        : null;
    const { newsError } = this.state;

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
          <DateOnlyPicker
            value={date}
            maxDate={TeamActivitySnapshot.today()}
            onChange={(value) => {
              this.selectDate(value);
            }}
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
                key={`${String(activity.scopeRevision)}:${date}`}
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

    this.request?.abort();
    const request = new AbortController();
    this.request = request;
    const generation = ++this.generation;
    const date = this.state.date;
    this.setState({
      loading: true,
      error: preserveError ? this.state.error : null,
    });
    void this.loadNews();
    try {
      const activity = TeamActivityContext.require(this.context);
      if (date === activity.date) {
        await activity.refresh();
      } else {
        const brief = await activity.service.brief(date, request.signal);
        if (this.isCurrent(generation)) {
          const scoped = activity.overview
            ? TeamActivitySnapshot.scope(activity.overview, brief)
            : { ...brief, runs: [] };
          this.setState({
            brief: scoped,
            scopeRevision: activity.scopeRevision,
          });
        }
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
        this.setState({ loading: false });
      }
    }
  }

  private async loadNews(): Promise<void> {
    if (!this.mounted || this.newsRequest) {
      return;
    }

    const request = new AbortController();
    this.newsRequest = request;
    const activity = TeamActivityContext.require(this.context);
    try {
      const news = await activity.service.news(request.signal);
      if (
        this.isNewsCurrent(request) &&
        activity.scopeRevision ===
          TeamActivityContext.require(this.context).scopeRevision
      ) {
        this.setState({
          news,
          newsError: null,
          newsScopeRevision: activity.scopeRevision,
        });
      }
    } catch (cause) {
      if (this.isNewsCurrent(request)) {
        this.setState({
          news: null,
          newsError:
            cause instanceof Error ? cause.message : 'Source unavailable.',
        });
      }
    } finally {
      if (this.newsRequest === request) {
        this.newsRequest = null;
      }
    }
  }

  private isNewsCurrent(request: AbortController): boolean {
    return (
      this.mounted && this.newsRequest === request && !request.signal.aborted
    );
  }

  private async controlCompany(): Promise<void> {
    const activity = TeamActivityContext.require(this.context);
    const company = activity.overview?.company;
    if (!company || this.action) {
      return;
    }

    this.request?.abort();
    this.generation += 1;
    const action = new AbortController();
    this.action = action;
    this.setState({ controlling: true, error: null });
    try {
      await activity.service.setCompanyEnabled(
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
}
