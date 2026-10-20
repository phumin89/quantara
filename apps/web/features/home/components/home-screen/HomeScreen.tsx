'use client';

import { Component, type ContextType } from 'react';
import NextLink from 'next/link';
import {
  Alert,
  Box,
  Button,
  Chip,
  Link,
  Stack,
  Typography,
} from '@mui/material';
import RefreshOutlined from '@mui/icons-material/RefreshOutlined';
import PauseOutlined from '@mui/icons-material/PauseOutlined';
import PlayArrowOutlined from '@mui/icons-material/PlayArrowOutlined';
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined';
import { TeamServiceContext } from '../../../team/context/TeamServiceContext';
import { TeamRole } from '../../../team/contracts/TeamRole';
import { OfficeStateAdapter } from '../../presentation/OfficeStateAdapter';
import { OfficePresentation } from '../../presentation/OfficePresentation';
import { OfficeCanvas } from '../office-canvas/OfficeCanvas';
import type { HomeScreenState } from './HomeScreenState';

export default class HomeScreen extends Component<
  Record<string, never>,
  HomeScreenState
> {
  public static override contextType = TeamServiceContext.value;

  declare public context: ContextType<typeof TeamServiceContext.value>;

  public override state: HomeScreenState = {
    overview: null,
    runs: [],
    loading: true,
    error: null,
    fetchedAt: null,
    now: 0,
    selectedRole: TeamRole.Reviewer,
    paused: false,
  };

  private controller: AbortController | null = null;

  private poll: ReturnType<typeof setInterval> | null = null;

  private mounted = false;

  public override componentDidMount(): void {
    this.mounted = true;
    document.addEventListener('visibilitychange', this.visibility);
    void this.load();
    this.poll = setInterval(() => {
      if (!document.hidden) {
        this.setState({ now: Date.now() });
        void this.load();
      }
    }, 10000);
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.controller?.abort();
    if (this.poll) {
      clearInterval(this.poll);
    }

    document.removeEventListener('visibilitychange', this.visibility);
  }

  private readonly visibility = (): void => {
    if (document.hidden) {
      this.controller?.abort();
    } else {
      this.setState({ now: Date.now() });
      void this.load();
    }
  };

  private async load(): Promise<void> {
    if (this.controller) {
      return;
    }

    const controller = new AbortController();
    this.controller = controller;
    const timeout = setTimeout(() => {
      controller.abort();
    }, 8000);
    try {
      const service = TeamServiceContext.require(this.context);
      const [overview, brief] = await Promise.all([
        service.get(controller.signal),
        service.brief(OfficeStateAdapter.date(), controller.signal),
      ]);
      if (!this.mounted || controller.signal.aborted) {
        return;
      }

      if (OfficeStateAdapter.isOlder(this.state.overview, overview)) {
        throw new Error(
          'An older team snapshot was received. Retaining the last verified state.',
        );
      }

      const selectedRole =
        this.state.fetchedAt === null
          ? (overview.company?.activeRole ??
            overview.latestRun?.steps[0]?.role ??
            TeamRole.Reviewer)
          : this.state.selectedRole;
      this.setState({
        overview,
        runs: [...brief.runs]
          .sort(
            (left, right) =>
              Date.parse(right.updatedAt) - Date.parse(left.updatedAt),
          )
          .slice(0, 100),
        fetchedAt: Date.now(),
        now: Date.now(),
        loading: false,
        error: null,
        selectedRole,
      });
    } catch (error) {
      if (this.mounted && !document.hidden) {
        this.setState({
          loading: false,
          now: Date.now(),
          error:
            error instanceof Error
              ? OfficeStateAdapter.clean(error.message)
              : 'Unable to load research status.',
        });
      }
    } finally {
      clearTimeout(timeout);
      if (this.controller === controller) {
        this.controller = null;
      }
    }
  }

  public override render() {
    const {
      overview,
      runs,
      loading,
      error,
      fetchedAt,
      now,
      selectedRole,
      paused,
    } = this.state;
    const fresh =
      fetchedAt !== null && error === null && now - fetchedAt <= 30000;
    const agents = OfficeStateAdapter.project(overview, runs, fresh);
    const selected = agents.find((agent) => agent.role === selectedRole);
    const company = overview?.company;
    const connectionLabel = fresh ? 'Connected' : 'Stale / unavailable';

    return (
      <Box sx={{ width: '100%', maxWidth: 1280 }}>
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          sx={{ mb: 3, justifyContent: 'space-between', gap: 2 }}
        >
          <Box>
            <Typography
              component="h1"
              variant="h1"
              sx={{ m: 0 }}
            >
              Home
            </Typography>
            <Typography
              color="text.secondary"
              sx={{ mt: 1 }}
            >
              Your research team, in view.
            </Typography>
          </Box>
          <Stack
            direction="row"
            sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}
          >
            <Button
              variant="outlined"
              startIcon={<RefreshOutlined />}
              onClick={() => {
                void this.load();
              }}
            >
              Refresh status
            </Button>
            <Button
              component={NextLink}
              href="/daily-brief"
              endIcon={<ArrowForwardOutlined />}
            >
              Daily Brief
            </Button>
          </Stack>
        </Stack>
        {error && (
          <Alert
            severity="warning"
            sx={{ mb: 2 }}
          >
            {error}{' '}
            {fetchedAt !== null
              ? 'Showing the last known records; activity is marked stale.'
              : 'No verified team state is available yet.'}
          </Alert>
        )}
        <Box
          component="section"
          aria-label="Research operation"
          sx={{
            mb: 3,
            display: 'flex',
            flexWrap: 'wrap',
            gap: 1.5,
            alignItems: 'center',
          }}
        >
          <Chip
            size="small"
            label={loading ? 'Checking' : connectionLabel}
            color={fresh ? 'success' : 'default'}
            variant="outlined"
          />
          <Typography>
            {loading
              ? 'Checking the recorded research state…'
              : OfficePresentation.company(company)}
          </Typography>
          <Typography
            color="text.secondary"
            variant="body2"
            sx={{ width: '100%' }}
          >
            Last checked: {OfficePresentation.time(fetchedAt)}
            {company?.nextRunAt
              ? ` · Next assessment check: ${OfficePresentation.time(company.nextRunAt)}`
              : ''}
          </Typography>
        </Box>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: {
              xs: 'minmax(0, 1fr)',
              lg: 'minmax(0, 1fr) 280px',
            },
            border: '1px solid',
            borderColor: 'divider',
            borderRadius: '8px',
            overflow: 'hidden',
          }}
        >
          <Box
            component="section"
            aria-labelledby="office-heading"
            sx={{ minWidth: 0 }}
          >
            <Stack
              direction="row"
              sx={{
                alignItems: 'center',
                justifyContent: 'space-between',
                px: 2,
                py: 1.5,
                borderBottom: '1px solid',
                borderColor: 'divider',
              }}
            >
              <Typography
                component="h2"
                id="office-heading"
                sx={{ fontSize: 18, fontWeight: 600 }}
              >
                Research office
              </Typography>
              <Button
                size="small"
                startIcon={paused ? <PlayArrowOutlined /> : <PauseOutlined />}
                onClick={() => {
                  this.setState({ paused: !paused });
                }}
              >
                {paused ? 'Resume animation' : 'Pause animation'}
              </Button>
            </Stack>
            <OfficeCanvas
              agents={agents}
              selectedId={selected?.id ?? null}
              paused={paused}
              onSelect={(id) => {
                const agent = agents.find((item) => item.id === id);
                if (agent) {
                  this.setState({ selectedRole: agent.role });
                }
              }}
            />
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ px: 2, py: 2 }}
            >
              Only a recorded running step animates. Select a character or a
              role for its evidence. Research never authorizes trades.
            </Typography>
          </Box>
          <Box
            component="section"
            aria-labelledby="roles-heading"
            sx={{
              borderLeft: { lg: '1px solid' },
              borderTop: { xs: '1px solid', lg: 0 },
              borderColor: 'divider',
            }}
          >
            <Typography
              component="h2"
              id="roles-heading"
              sx={{ fontSize: 18, fontWeight: 600, px: 2, py: 2 }}
            >
              Team
            </Typography>
            {agents.map((agent) => (
              <Button
                key={agent.role}
                aria-pressed={agent.role === selectedRole}
                aria-controls="role-evidence"
                onClick={() => {
                  this.setState({ selectedRole: agent.role });
                }}
                sx={{
                  display: 'flex',
                  width: '100%',
                  justifyContent: 'space-between',
                  px: 2,
                  py: 1.5,
                  borderRadius: 0,
                  bgcolor:
                    agent.role === selectedRole
                      ? 'action.selected'
                      : 'transparent',
                  gap: 1,
                }}
              >
                <Typography
                  component="span"
                  variant="body2"
                  sx={{ fontWeight: 600 }}
                >
                  {agent.label}
                </Typography>
                <Chip
                  component="span"
                  size="small"
                  label={OfficePresentation.label(agent.status)}
                  color={OfficePresentation.color(agent.status)}
                  variant="outlined"
                />
              </Button>
            ))}
          </Box>
        </Box>
        <Box
          component="section"
          id="role-evidence"
          aria-labelledby="evidence-heading"
          sx={{ mt: 4 }}
        >
          <Stack
            direction="row"
            sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}
          >
            <Typography
              component="h2"
              id="evidence-heading"
              sx={{ fontSize: 22, fontWeight: 600 }}
            >
              {selected?.label ?? 'Role'} evidence
            </Typography>
            {selected && (
              <Chip
                size="small"
                label={OfficePresentation.label(selected.status)}
                color={OfficePresentation.color(selected.status)}
                variant="outlined"
              />
            )}
          </Stack>
          <Typography
            color="text.secondary"
            variant="body2"
            sx={{ mt: 1 }}
          >
            Last recorded: {OfficePresentation.time(selected?.updatedAt)}
          </Typography>
          <Typography
            sx={{
              mt: 2,
              maxWidth: '75ch',
              overflowWrap: 'anywhere',
              whiteSpace: 'pre-wrap',
            }}
          >
            {selected?.summary ??
              (loading
                ? 'Loading recorded work…'
                : 'No recorded work for this role today. Idle is not a completed report.')}
          </Typography>
          {company?.error && (
            <Alert
              severity="error"
              sx={{ mt: 2 }}
            >
              {OfficeStateAdapter.clean(company.error)}
            </Alert>
          )}
          {selected?.date && selected.runId && (
            <Link
              component={NextLink}
              href={`/daily-brief?date=${selected.date}&view=team-chat#run-${selected.runId}`}
              sx={{ display: 'inline-block', mt: 2, py: 1.5 }}
            >
              Open recorded report in Daily Brief
            </Link>
          )}
        </Box>
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ mt: 5 }}
        >
          Office renderer:{' '}
          <Link
            href="https://github.com/pixel-agents-hq/pixel-agents"
            target="_blank"
            rel="noopener noreferrer"
          >
            Pixel Agents
          </Link>{' '}
          ·{' '}
          <Link href="/vendor/pixel-agents/NOTICE.txt">
            Licenses and asset credits
          </Link>
        </Typography>
      </Box>
    );
  }
}
