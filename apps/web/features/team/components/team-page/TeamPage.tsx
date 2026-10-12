'use client';

import {
  Component,
  type ChangeEvent,
  type ContextType,
  type ReactNode,
} from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import PlayArrowOutlinedIcon from '@mui/icons-material/PlayArrowOutlined';
import StopOutlinedIcon from '@mui/icons-material/StopOutlined';
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined';
import { TeamServiceContext } from '../../context/TeamServiceContext';
import type { TeamRun } from '../../contracts/TeamRun';
import { TeamRole } from '../../contracts/TeamRole';
import { TeamRunStatus } from '../../contracts/TeamRunStatus';
import type { TeamPageState } from './TeamPageState';

export default class TeamPage extends Component<object, TeamPageState> {
  public static override readonly contextType = TeamServiceContext.value;

  private static readonly roleDetails = [
    {
      role: TeamRole.Ceo,
      label: 'CEO',
      responsibility: 'Sets direction and coordinates the team.',
    },
    {
      role: TeamRole.Analyst,
      label: 'Analyst',
      responsibility: 'Analyzes portfolio data against the plan.',
    },
    {
      role: TeamRole.Reviewer,
      label: 'Reviewer',
      responsibility: 'Checks the work and identifies risks.',
    },
  ] as const;

  public override context: ContextType<typeof TeamServiceContext.value> = null;

  public override state: TeamPageState = {
    overview: null,
    run: null,
    objective: '',
    loading: true,
    submitting: false,
    stopping: false,
    error: null,
  };

  private mounted: boolean = false;

  private generation: number = 0;

  private pollTimer: number | null = null;

  private request: AbortController | null = null;

  private actionRequest: AbortController | null = null;

  private actionInFlight: boolean = false;

  private get service(): NonNullable<
    ContextType<typeof TeamServiceContext.value>
  > {
    return TeamServiceContext.require(this.context);
  }

  public override componentDidMount(): void {
    this.mounted = true;
    void this.load();
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.stopPolling();
    this.request?.abort();
    this.actionRequest?.abort();
  }

  public override render(): ReactNode {
    const { overview, run, objective, loading, submitting, stopping, error } =
      this.state;
    const active = this.isActive(run?.status);
    const canStart =
      Boolean(overview?.configured) &&
      objective.trim().length > 0 &&
      !active &&
      !submitting &&
      !loading;

    return (
      <Stack
        spacing={3}
        sx={{ width: '100%', maxWidth: 1280, mx: 'auto' }}
      >
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            flexWrap: 'wrap',
            gap: 1.5,
          }}
        >
          <GroupsOutlinedIcon aria-hidden="true" />
          <Typography
            component="h1"
            sx={{ fontSize: 32, fontWeight: 700 }}
          >
            AI team
          </Typography>
          <Chip
            label={this.configurationLabel(loading, overview?.configured)}
            size="small"
            color={this.configurationColor(loading, overview?.configured)}
            sx={this.statusStyle(
              this.configurationStatus(loading, overview?.configured),
            )}
          />
          {run && (
            <Chip
              label={run.status}
              size="small"
              color={this.statusColor(run.status)}
              sx={this.statusStyle(run.status)}
            />
          )}
        </Box>
        <Paper
          variant="outlined"
          sx={{ p: { xs: 2, md: 3 }, borderRadius: '8px' }}
        >
          <Stack spacing={2.5}>
            {!loading && overview && !overview.configured && (
              <Alert severity="info">
                Configure the Codex worker on the server to start tasks.
              </Alert>
            )}
            {error && (
              <Alert
                severity="error"
                action={
                  <Button
                    color="inherit"
                    size="small"
                    onClick={() => void this.load()}
                  >
                    Retry
                  </Button>
                }
              >
                {error}
              </Alert>
            )}
            <Typography
              variant="body2"
              color="text.secondary"
            >
              Reports only; no trades are executed.
            </Typography>
            <TextField
              label="Objective"
              value={objective}
              onChange={this.handleObjectiveChange}
              slotProps={{ htmlInput: { maxLength: 2000 } }}
              multiline
              minRows={4}
              fullWidth
              placeholder="Describe the outcome the team should work toward."
              disabled={submitting || active}
            />
            <Stack
              direction={{ xs: 'column', sm: 'row' }}
              spacing={1}
            >
              <Button
                variant="contained"
                startIcon={
                  submitting ? (
                    <CircularProgress
                      size={16}
                      color="inherit"
                    />
                  ) : (
                    <PlayArrowOutlinedIcon />
                  )
                }
                disabled={!canStart}
                sx={{ minHeight: 44, textTransform: 'none' }}
                onClick={() => void this.start()}
              >
                Start task
              </Button>
              <Button
                variant="outlined"
                color="error"
                startIcon={<StopOutlinedIcon />}
                disabled={!active || stopping || submitting}
                sx={{ minHeight: 44, textTransform: 'none' }}
                onClick={() => void this.stop()}
              >
                Stop
              </Button>
            </Stack>
          </Stack>
        </Paper>
        <Paper
          variant="outlined"
          sx={{ p: { xs: 2, md: 3 }, borderRadius: '8px' }}
        >
          <Stack spacing={2}>
            <Typography
              component="h2"
              sx={{ fontSize: 18, fontWeight: 700 }}
            >
              Team
            </Typography>
            {run && (
              <Typography
                variant="body2"
                color="text.secondary"
              >
                Objective: {run.objective}
              </Typography>
            )}
            {TeamPage.roleDetails.map((detail) => {
              const step = run?.steps.find((item) => item.role === detail.role);

              return (
                <Box
                  key={detail.role}
                  sx={{ py: 1.5 }}
                >
                  <Box
                    sx={{
                      display: 'flex',
                      flexDirection: { xs: 'column', sm: 'row' },
                      gap: 1,
                      justifyContent: 'space-between',
                      alignItems: { xs: 'flex-start', sm: 'center' },
                    }}
                  >
                    <Box>
                      <Typography sx={{ fontWeight: 700 }}>
                        {detail.label}
                      </Typography>
                      <Typography
                        variant="body2"
                        color="text.secondary"
                      >
                        {detail.responsibility}
                      </Typography>
                    </Box>
                    <Chip
                      label={step?.status ?? 'waiting'}
                      size="small"
                      color={this.statusColor(step?.status)}
                      sx={this.statusStyle(step?.status)}
                    />
                  </Box>
                  {step?.output && (
                    <Typography
                      sx={{
                        mt: 1,
                        whiteSpace: 'pre-wrap',
                        overflowWrap: 'anywhere',
                      }}
                    >
                      {step.output}
                    </Typography>
                  )}
                  {step?.error && (
                    <Typography
                      color="error"
                      sx={{ mt: 1, whiteSpace: 'pre-wrap' }}
                    >
                      {step.error}
                    </Typography>
                  )}
                </Box>
              );
            })}
            {!run && (
              <Typography color="text.secondary">
                Give the team a goal to begin.
              </Typography>
            )}
            {run?.error && <Alert severity="error">{run.error}</Alert>}
          </Stack>
        </Paper>
        {run && (
          <Typography
            variant="caption"
            color="text.secondary"
          >
            Updated {new Date(run.updatedAt).toLocaleString()}
          </Typography>
        )}
      </Stack>
    );
  }

  private readonly handleObjectiveChange = (
    event: ChangeEvent<HTMLInputElement>,
  ): void => {
    this.setState({ objective: event.target.value.slice(0, 2000) });
  };

  private async load(preserveError: boolean = false): Promise<void> {
    if (!this.mounted || this.actionInFlight) {
      return;
    }

    this.stopPolling();
    const generation = ++this.generation;
    this.request?.abort();
    const request = new AbortController();
    this.request = request;
    this.setState({
      loading: this.state.overview === null,
      error: preserveError ? this.state.error : null,
    });
    try {
      const overview = await this.service.get(request.signal);
      if (this.isCurrent(generation)) {
        this.setState({ overview, run: overview.latestRun, loading: false });
      }
    } catch (cause) {
      if (this.isCurrent(generation) && !request.signal.aborted) {
        this.setState({
          loading: false,
          error:
            cause instanceof Error ? cause.message : 'Unable to load AI team.',
        });
      }
    } finally {
      if (this.isCurrent(generation)) {
        this.request = null;
        this.setState({ loading: false }, () => {
          this.schedulePolling();
        });
      }
    }
  }

  private async start(): Promise<void> {
    if (
      this.actionInFlight ||
      this.state.loading ||
      !this.state.overview?.configured ||
      this.isActive(this.state.run?.status) ||
      !this.state.objective.trim()
    ) {
      return;
    }

    const generation = ++this.generation;
    this.stopPolling();
    this.request?.abort();
    const action = new AbortController();
    this.actionRequest = action;
    this.actionInFlight = true;
    this.setState({ submitting: true, error: null });
    let reconcile = false;
    try {
      const run = await this.service.start(
        this.state.objective.trim(),
        action.signal,
      );
      if (this.mounted && generation === this.generation) {
        this.setState({ run });
      }
    } catch (cause) {
      reconcile = true;
      if (
        this.mounted &&
        generation === this.generation &&
        !action.signal.aborted
      ) {
        this.setState({
          error:
            cause instanceof Error ? cause.message : 'Unable to start task.',
        });
      }
    } finally {
      if (this.actionRequest === action) {
        this.actionInFlight = false;
        this.actionRequest = null;
      }

      if (this.mounted && generation === this.generation) {
        this.setState({ submitting: false }, () => {
          if (reconcile) {
            void this.load(true);
          } else {
            this.schedulePolling();
          }
        });
      }
    }
  }

  private async stop(): Promise<void> {
    if (
      this.actionInFlight ||
      !this.isActive(this.state.run?.status) ||
      !this.state.run
    ) {
      return;
    }

    const generation = ++this.generation;
    this.stopPolling();
    this.request?.abort();
    const action = new AbortController();
    this.actionRequest = action;
    this.actionInFlight = true;
    this.setState({ stopping: true, error: null });
    let reconcile = false;
    try {
      const run = await this.service.stop(this.state.run.id, action.signal);
      if (this.mounted && generation === this.generation) {
        this.setState({ run });
      }
    } catch (cause) {
      reconcile = true;
      if (
        this.mounted &&
        generation === this.generation &&
        !action.signal.aborted
      ) {
        this.setState({
          error:
            cause instanceof Error ? cause.message : 'Unable to stop task.',
        });
      }
    } finally {
      if (this.actionRequest === action) {
        this.actionInFlight = false;
        this.actionRequest = null;
      }

      if (this.mounted && generation === this.generation) {
        this.setState({ stopping: false }, () => {
          if (reconcile) {
            void this.load(true);
          } else {
            this.schedulePolling();
          }
        });
      }
    }
  }

  private schedulePolling(): void {
    this.stopPolling();
    if (
      this.mounted &&
      !this.actionInFlight &&
      this.isActive(this.state.run?.status)
    ) {
      this.pollTimer = window.setTimeout(() => void this.load(), 2000);
    }
  }

  private isActive(status: TeamRunStatus | undefined): boolean {
    return status === TeamRunStatus.Queued || status === TeamRunStatus.Running;
  }

  private isCurrent(generation: number): boolean {
    return this.mounted && generation === this.generation;
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      window.clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private statusColor(
    status: TeamRun['status'] | undefined,
  ): 'default' | 'success' | 'warning' | 'error' {
    if (status === TeamRunStatus.Completed) {
      return 'success';
    }

    if (status === TeamRunStatus.Running || status === TeamRunStatus.Queued) {
      return 'warning';
    }

    if (status === TeamRunStatus.Failed) {
      return 'error';
    }

    return 'default';
  }

  private configurationLabel(
    loading: boolean,
    configured: boolean | undefined,
  ): string {
    if (loading) {
      return 'Checking';
    }

    if (configured === undefined) {
      return 'Unavailable';
    }

    return configured ? 'Configured' : 'Not configured';
  }

  private configurationStatus(
    loading: boolean,
    configured: boolean | undefined,
  ): TeamRunStatus | undefined {
    if (loading) {
      return undefined;
    }

    if (configured === undefined) {
      return TeamRunStatus.Failed;
    }

    return configured ? TeamRunStatus.Completed : TeamRunStatus.Queued;
  }

  private statusStyle(status: TeamRunStatus | undefined) {
    switch (status) {
      case TeamRunStatus.Completed:
        return { color: '#166534', bgcolor: '#dcfce7' };
      case TeamRunStatus.Queued:
      case TeamRunStatus.Running:
        return { color: '#92400e', bgcolor: '#fef3c7' };
      case TeamRunStatus.Failed:
        return { color: '#991b1b', bgcolor: '#fee2e2' };
      case TeamRunStatus.Cancelled:
      case undefined:
        return { color: '#525252', bgcolor: '#f3f3f3' };
    }
  }

  private configurationColor(
    loading: boolean,
    configured: boolean | undefined,
  ): 'default' | 'success' | 'warning' {
    if (loading) {
      return 'default';
    }

    return configured ? 'success' : 'warning';
  }
}
