'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import AutoStoriesOutlinedIcon from '@mui/icons-material/AutoStoriesOutlined';
import { TeamActivityContext } from '../../context/TeamActivityContext';
import { TeamActivitySnapshot } from '../../presentation/TeamActivitySnapshot';
import { CompanyLoopStatus } from '../../contracts/CompanyLoopStatus';
import type { DailyBriefPageState } from './DailyBriefPageState';
import DailyBriefNews from '../daily-brief-news/DailyBriefNews';
import DailyBriefConversation from '../daily-brief-conversation/DailyBriefConversation';
import TeamResearchStatus from '../team-research-status/TeamResearchStatus';
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
    controlling: false,
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
    window.addEventListener('popstate', this.syncLocation);
    window.addEventListener('quantara:route-change', this.syncLocation);
    this.syncLocation();
  }

  private readonly syncLocation = (): void => {
    const query = new URLSearchParams(window.location.search);
    const date = query.get('date') ?? '';
    const validDate =
      /^\d{4}-\d{2}-\d{2}$/.test(date) &&
      !Number.isNaN(Date.parse(`${date}T00:00:00Z`)) &&
      new Date(`${date}T00:00:00Z`).toISOString().slice(0, 10) === date &&
      date >= '1970-01-01' &&
      date <= TeamActivitySnapshot.today();
    const selectedDate = validDate ? date : TeamActivitySnapshot.today();
    if (selectedDate !== this.state.date) {
      this.selectDate(selectedDate, false);
    }
  };

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
    window.removeEventListener('popstate', this.syncLocation);
    window.removeEventListener('quantara:route-change', this.syncLocation);
  }

  public override render(): ReactNode {
    const { date, controlling } = this.state;
    const activity = TeamActivityContext.require(this.context);
    const current = date === activity.date;
    let brief =
      this.state.scopeRevision === activity.scopeRevision
        ? this.state.brief
        : null;
    if (current) {
      brief = activity.brief;
    }

    const loading = current ? activity.loading : this.state.loading;
    const error = this.state.error ?? (current ? activity.error : null);
    const news =
      this.state.newsScopeRevision === activity.scopeRevision
        ? this.state.news
        : null;

    return (
      <Stack
        spacing="var(--section-gap)"
        sx={{
          '--panel-padding': { xs: '12px', sm: '16px' },
          '--section-gap': { xs: '16px', sm: '12px' },
          width: '100%',
          maxWidth: 1280,
          minWidth: 0,
          '& summary': {
            minHeight: 44,
            alignContent: 'center',
            cursor: 'pointer',
            borderRadius: 'var(--radius-control)',
            '&:focus-visible': {
              outline: '2px solid',
              outlineColor: 'info.main',
              outlineOffset: 3,
            },
          },
        }}
      >
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          sx={{
            alignItems: { sm: 'center' },
            justifyContent: 'space-between',
            gap: { xs: 1, sm: 2 },
            p: 'var(--panel-padding)',
            border: '1px solid var(--border-panel)',
            borderRadius: 'var(--radius-panel)',
            bgcolor: 'var(--surface-panel)',
            backdropFilter: 'blur(var(--glass-blur))',
            boxShadow: 'var(--shadow-panel)',
          }}
        >
          <Stack
            direction="row"
            spacing={2}
            sx={{ alignItems: 'center', minWidth: 0 }}
          >
            <Box
              aria-hidden="true"
              sx={{
                display: { xs: 'none', sm: 'grid' },
                placeItems: 'center',
                width: 56,
                height: 56,
                flexShrink: 0,
                borderRadius: 'var(--radius-control)',
                bgcolor: 'var(--color-mint)',
                color: 'text.primary',
                transform: 'rotate(-4deg)',
              }}
            >
              <AutoStoriesOutlinedIcon sx={{ fontSize: 28 }} />
            </Box>
            <Box sx={{ minWidth: 0 }}>
              <Typography
                component="h1"
                sx={{
                  fontSize: { xs: 26, sm: 36 },
                  fontWeight: 600,
                  lineHeight: 1.2,
                  overflowWrap: 'break-word',
                }}
              >
                Daily Brief
              </Typography>
              <Typography
                variant="body2"
                color="text.secondary"
                sx={{
                  mt: { xs: 0.25, sm: 0.75 },
                  fontSize: { xs: 12, sm: 14 },
                  lineHeight: 1.5,
                }}
              >
                Research reports and evidence · Bangkok time
              </Typography>
            </Box>
          </Stack>
          <DateOnlyPicker
            label="Report date"
            value={date}
            maxDate={TeamActivitySnapshot.today()}
            onChange={(value) => {
              this.selectDate(value);
            }}
          />
        </Stack>
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
        <TeamResearchStatus
          company={activity.overview?.company}
          loading={loading}
          controlling={controlling}
          onControl={() => void this.controlCompany()}
        />
        <DailyBriefConversation
          key={String(activity.scopeRevision) + ':' + date}
          active
          brief={brief}
          loading={loading}
          error={error}
          date={date}
          archive
        />
        {current ? (
          <Box
            component="details"
            sx={{
              p: 'var(--panel-padding)',
              border: '1px solid var(--border-panel)',
              borderRadius: 'var(--radius-panel)',
              bgcolor: 'var(--surface-panel)',
            }}
          >
            <Box
              component="summary"
              sx={{ fontWeight: 600 }}
            >
              Current news sources
            </Box>
            <DailyBriefNews
              news={news}
              error={this.state.newsError}
            />
          </Box>
        ) : (
          <Typography
            variant="caption"
            color="text.secondary"
          >
            Historical news sources are not retained for this date.
          </Typography>
        )}
      </Stack>
    );
  }

  private selectDate(date: string, updateLocation: boolean = true): void {
    if (updateLocation) {
      const location = new URL(window.location.href);
      if (date) {
        location.searchParams.set('date', date);
      } else {
        location.searchParams.delete('date');
      }

      location.hash = '';
      window.history.replaceState(window.history.state, '', location.href);
    }

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

  private async load(
    preserveError: boolean = false,
    forceStatus: boolean = false,
  ): Promise<void> {
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
      let activity = TeamActivityContext.require(this.context);
      if (date === activity.date) {
        await activity.refresh(forceStatus);
      } else {
        if (forceStatus || !activity.overview?.latestRun || activity.error) {
          await activity.refresh(forceStatus);
          if (!this.isCurrent(generation)) {
            return;
          }

          activity = TeamActivityContext.require(this.context);
        }

        if (!activity.overview || activity.error) {
          throw new Error(
            'Research account could not be verified. Retry to load this report.',
          );
        }

        const brief = await activity.service.brief(date, request.signal);
        if (this.isCurrent(generation)) {
          if (!activity.overview.latestRun && brief.runs.length > 0) {
            throw new Error(
              'Report account could not be verified. Retry to refresh research status.',
            );
          }

          const scoped = TeamActivitySnapshot.scope(activity.overview, brief);
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
    if (!company || this.action || this.state.controlling) {
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
      await this.load(true, true);
      if (this.mounted) {
        this.setState({ controlling: false });
      }
    }
  }

  private isCurrent(generation: number): boolean {
    return this.mounted && generation === this.generation;
  }
}
