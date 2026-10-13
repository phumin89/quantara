'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Chip,
  CircularProgress,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import ForumOutlinedIcon from '@mui/icons-material/ForumOutlined';
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined';
import AssignmentOutlinedIcon from '@mui/icons-material/AssignmentOutlined';
import StopOutlinedIcon from '@mui/icons-material/StopOutlined';
import PlayArrowOutlinedIcon from '@mui/icons-material/PlayArrowOutlined';
import { TeamServiceContext } from '../../context/TeamServiceContext';
import { TeamRole } from '../../contracts/TeamRole';
import { TeamRunStatus } from '../../contracts/TeamRunStatus';
import { CompanyLoopStatus } from '../../contracts/CompanyLoopStatus';
import type { DailyBriefPageState } from './DailyBriefPageState';

export default class DailyBriefPage extends Component<
  object,
  DailyBriefPageState
> {
  public static override readonly contextType = TeamServiceContext.value;

  private static readonly members = [
    {
      role: TeamRole.Ceo,
      name: 'CEO',
      initials: 'C',
      responsibility: 'Direction & coordination',
      foreground: '#1e40af',
      background: '#dbeafe',
    },
    {
      role: TeamRole.Analyst,
      name: 'Analyst',
      initials: 'A',
      responsibility: 'Portfolio research & analysis',
      foreground: '#6b21a8',
      background: '#f3e8ff',
    },
    {
      role: TeamRole.Reviewer,
      name: 'Reviewer',
      initials: 'R',
      responsibility: 'Evidence & risk review',
      foreground: '#166534',
      background: '#dcfce7',
    },
  ] as const;

  public override context: ContextType<typeof TeamServiceContext.value> = null;

  public override state: DailyBriefPageState = {
    date: '',
    brief: null,
    overview: null,
    loading: true,
    error: null,
    membersOpen: false,
    controlling: false,
  };

  private mounted: boolean = false;

  private generation: number = 0;

  private request: AbortController | null = null;

  private timer: number | null = null;

  private action: AbortController | null = null;

  public override componentDidMount(): void {
    this.mounted = true;
    this.setState({ date: DailyBriefPage.today() }, () => void this.load());
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.request?.abort();
    this.action?.abort();
    this.clearTimer();
  }

  public override render(): ReactNode {
    const { date, brief, loading, error, membersOpen, controlling, overview } =
      this.state;
    const company = overview?.company;
    const latest = brief?.runs.at(-1);
    const reports =
      brief?.runs.flatMap((run) => run.steps.filter((step) => step.output)) ??
      [];

    return (
      <Stack
        spacing={3}
        sx={{ width: '100%', maxWidth: 1280, mx: 'auto' }}
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
            >
              A day in the team&apos;s work.
            </Typography>
          </Box>
          <TextField
            type="date"
            label="Date"
            value={date}
            slotProps={{
              inputLabel: { shrink: true },
              htmlInput: { max: DailyBriefPage.today(), min: '1970-01-01' },
            }}
            onChange={(event) => {
              this.selectDate(event.target.value);
            }}
            sx={{ width: { xs: '100%', sm: 190 } }}
          />
        </Box>
        <Box
          sx={{
            display: 'flex',
            flexWrap: 'wrap',
            gap: 1.5,
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <Stack spacing={0.5}>
            <Typography
              variant="body2"
              sx={{ fontWeight: 600 }}
            >
              {this.companyLabel(company?.status)}
            </Typography>
            <Typography
              variant="caption"
              color="text.secondary"
            >
              Automatic research · Every hour · No trades
            </Typography>
            {company?.status === CompanyLoopStatus.Waiting &&
              company.nextRunAt && (
                <Typography
                  variant="caption"
                  color="text.secondary"
                >
                  Next review{' '}
                  {new Date(company.nextRunAt).toLocaleString('en-GB', {
                    timeZone: 'Asia/Bangkok',
                  })}{' '}
                  Bangkok
                </Typography>
              )}
          </Stack>
          <Button
            variant="outlined"
            color={
              company?.enabled || company?.status === CompanyLoopStatus.Error
                ? 'error'
                : 'primary'
            }
            startIcon={
              company?.enabled ||
              company?.status === CompanyLoopStatus.Error ? (
                <StopOutlinedIcon />
              ) : (
                <PlayArrowOutlinedIcon />
              )
            }
            disabled={!company || controlling}
            onClick={() => void this.controlCompany()}
            sx={{ minHeight: 44, textTransform: 'none' }}
          >
            {company?.enabled || company?.status === CompanyLoopStatus.Error
              ? 'Stop company'
              : 'Resume company'}
          </Button>
        </Box>
        {company?.error && <Alert severity="warning">{company.error}</Alert>}
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
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: {
              xs: 'minmax(0, 1fr)',
              lg: 'minmax(0, 1fr) 260px',
            },
            alignItems: 'start',
            gap: 3,
          }}
        >
          <Stack
            spacing={3}
            sx={{ minWidth: 0, order: { xs: 1, lg: 0 } }}
          >
            <Paper
              variant="outlined"
              sx={{ borderRadius: '8px', overflow: 'hidden' }}
            >
              <Box
                sx={{
                  bgcolor: '#141414',
                  color: '#fff',
                  p: 2,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 1,
                }}
              >
                <ForumOutlinedIcon fontSize="small" />
                <Typography
                  component="h2"
                  sx={{ fontSize: 15, fontWeight: 600, flex: 1 }}
                >
                  Team conversation
                </Typography>
                <Typography variant="caption">
                  {reports.length} reports
                </Typography>
              </Box>
              <Box
                role="region"
                aria-label="Daily conversation"
                tabIndex={0}
                aria-busy={loading}
                sx={{
                  bgcolor: '#f6f6f6',
                  height: { xs: 420, md: 500 },
                  overflowY: 'auto',
                  p: { xs: 2, sm: 3 },
                  '&:focus-visible': {
                    outline: '2px solid #1e40af',
                    outlineOffset: -2,
                  },
                }}
              >
                {loading && !brief ? (
                  <Stack
                    spacing={2}
                    sx={{
                      height: '100%',
                      alignItems: 'center',
                      justifyContent: 'center',
                    }}
                  >
                    <CircularProgress size={24} />
                    <Typography color="text.secondary">
                      Loading daily brief…
                    </Typography>
                  </Stack>
                ) : (
                  this.renderConversation()
                )}
              </Box>
              <Box sx={{ px: 2, py: 1, borderTop: '1px solid #e0e0e0' }}>
                <Typography
                  variant="caption"
                  color="text.secondary"
                >
                  Recorded team reports · Dates in Bangkok time
                </Typography>
              </Box>
            </Paper>
            <Paper
              variant="outlined"
              sx={{ p: { xs: 2, sm: 3 }, borderRadius: '8px' }}
            >
              <Stack spacing={2}>
                <Stack
                  direction="row"
                  spacing={1}
                  sx={{ alignItems: 'center' }}
                >
                  <AssignmentOutlinedIcon fontSize="small" />
                  <Typography
                    component="h2"
                    sx={{ fontSize: 16, fontWeight: 700 }}
                  >
                    Daily summary
                  </Typography>
                </Stack>
                {this.renderSummary()}
              </Stack>
            </Paper>
          </Stack>
          <Paper
            component="aside"
            aria-label="Team members"
            variant="outlined"
            sx={{
              borderRadius: '8px',
              order: { xs: 0, lg: 1 },
              overflow: 'hidden',
            }}
          >
            <Button
              onClick={() => {
                this.setState({ membersOpen: !membersOpen });
              }}
              aria-expanded={membersOpen}
              startIcon={<GroupsOutlinedIcon />}
              sx={{
                display: { xs: 'flex', lg: 'none' },
                width: '100%',
                justifyContent: 'flex-start',
                px: 2,
                minHeight: 44,
                textTransform: 'none',
              }}
            >
              Members · 3
            </Button>
            <Box
              sx={{
                p: 2.5,
                display: { xs: membersOpen ? 'block' : 'none', lg: 'block' },
              }}
            >
              <Typography
                component="h2"
                sx={{ fontSize: 15, fontWeight: 700, mb: 3 }}
              >
                Members{' '}
                <Box
                  component="span"
                  sx={{ ml: 1, color: 'text.secondary', fontWeight: 400 }}
                >
                  3
                </Box>
              </Typography>
              <Stack spacing={3}>
                {DailyBriefPage.members.map((member) => {
                  const step = latest?.steps.find(
                    (item) => item.role === member.role,
                  );

                  return (
                    <Stack
                      key={member.role}
                      direction="row"
                      spacing={1.5}
                      sx={{ alignItems: 'flex-start' }}
                    >
                      <Avatar
                        sx={{
                          width: 34,
                          height: 34,
                          fontSize: 14,
                          fontWeight: 700,
                          bgcolor: member.background,
                          color: member.foreground,
                        }}
                      >
                        {member.initials}
                      </Avatar>
                      <Box sx={{ minWidth: 0 }}>
                        <Typography sx={{ fontSize: 14, fontWeight: 700 }}>
                          {member.name}
                        </Typography>
                        <Typography
                          variant="caption"
                          color="text.secondary"
                          sx={{ display: 'block', mb: 0.75 }}
                        >
                          {member.responsibility}
                        </Typography>
                        <Chip
                          size="small"
                          label={step?.status ?? 'No task'}
                          sx={{
                            height: 22,
                            fontSize: 11,
                            textTransform: 'capitalize',
                            ...this.statusStyle(step?.status),
                          }}
                        />
                      </Box>
                    </Stack>
                  );
                })}
              </Stack>
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{
                  display: 'block',
                  mt: 3,
                  pt: 2,
                  borderTop: '1px solid #e0e0e0',
                }}
              >
                {this.configurationLabel()}
              </Typography>
            </Box>
          </Paper>
        </Box>
      </Stack>
    );
  }

  private renderConversation(): ReactNode {
    const { brief, error, date } = this.state;
    if (!date) {
      return (
        <Typography color="text.secondary">
          Select a date to view the team’s work.
        </Typography>
      );
    }

    if (!brief?.runs.length) {
      return (
        <Stack
          spacing={1}
          sx={{
            height: '100%',
            textAlign: 'center',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <ForumOutlinedIcon sx={{ fontSize: 32, color: '#707070', mb: 1 }} />
          <Typography sx={{ fontWeight: 600 }}>
            {error ? 'Daily brief unavailable' : 'No activity'}
          </Typography>
          <Typography
            variant="body2"
            color="text.secondary"
          >
            {error
              ? 'Retry to load this day’s records.'
              : 'No recorded team tasks for this day.'}
          </Typography>
        </Stack>
      );
    }

    return (
      <Stack spacing={3}>
        {brief.runs.map((run) => (
          <Stack
            key={run.id}
            spacing={2}
          >
            <Box sx={{ pb: 1, borderBottom: '1px solid #e0e0e0' }}>
              <Typography
                variant="caption"
                color="text.secondary"
              >
                Task started{' '}
                {new Intl.DateTimeFormat('en-GB', {
                  timeZone: 'Asia/Bangkok',
                  hour: '2-digit',
                  minute: '2-digit',
                }).format(new Date(run.createdAt))}
              </Typography>
              <Typography
                variant="body2"
                sx={{ fontWeight: 600, overflowWrap: 'anywhere', mt: 0.5 }}
              >
                {run.objective}
              </Typography>
            </Box>
            {run.steps
              .filter((step) => step.output || step.error)
              .map((step) => {
                const member = DailyBriefPage.members.find(
                  (item) => item.role === step.role,
                );

                return (
                  <Stack
                    key={step.role}
                    direction="row"
                    spacing={1.5}
                    sx={{ alignItems: 'flex-start' }}
                  >
                    <Avatar
                      sx={{
                        width: 32,
                        height: 32,
                        fontSize: 13,
                        fontWeight: 700,
                        bgcolor: member?.background,
                        color: member?.foreground,
                      }}
                    >
                      {member?.initials}
                    </Avatar>
                    <Box sx={{ minWidth: 0, flex: 1 }}>
                      <Stack
                        direction="row"
                        spacing={1}
                        sx={{ mb: 0.75, alignItems: 'center' }}
                      >
                        <Typography sx={{ fontSize: 13, fontWeight: 700 }}>
                          {member?.name}
                        </Typography>
                        <Chip
                          size="small"
                          label={step.status}
                          sx={{
                            height: 20,
                            fontSize: 10,
                            textTransform: 'capitalize',
                            ...this.statusStyle(step.status),
                          }}
                        />
                      </Stack>
                      {step.output && (
                        <Box
                          sx={{
                            bgcolor: '#fff',
                            border: '1px solid #e0e0e0',
                            borderRadius: '6px',
                            p: 1.5,
                            maxWidth: '75ch',
                          }}
                        >
                          <Typography
                            variant="body2"
                            sx={{
                              whiteSpace: 'pre-wrap',
                              overflowWrap: 'anywhere',
                              lineHeight: 1.7,
                            }}
                          >
                            {step.output}
                          </Typography>
                        </Box>
                      )}
                      {step.error && (
                        <Typography
                          variant="body2"
                          color="error"
                          sx={{ mt: 1, overflowWrap: 'anywhere' }}
                        >
                          {step.error}
                        </Typography>
                      )}
                    </Box>
                  </Stack>
                );
              })}
            {!run.steps.some((step) => step.output || step.error) && (
              <Typography
                variant="body2"
                color="text.secondary"
              >
                No reports yet · {run.status}
              </Typography>
            )}
            {run.error && <Alert severity="error">{run.error}</Alert>}
          </Stack>
        ))}
      </Stack>
    );
  }

  private renderSummary(): ReactNode {
    const summaries =
      this.state.brief?.runs.filter((run) =>
        run.steps.some(
          (step) => step.role === TeamRole.Reviewer && step.output,
        ),
      ) ?? [];
    if (!summaries.length) {
      return (
        <Typography
          variant="body2"
          color="text.secondary"
        >
          {this.state.error
            ? 'Summary unavailable until records can be loaded.'
            : 'No reviewer summary recorded for this day.'}
        </Typography>
      );
    }

    return (
      <Stack spacing={2}>
        {summaries.map((run) => (
          <Box key={run.id}>
            <Typography
              variant="caption"
              color="text.secondary"
            >
              Reviewer report · {run.objective}
            </Typography>
            <Typography
              variant="body2"
              sx={{
                mt: 1,
                whiteSpace: 'pre-wrap',
                overflowWrap: 'anywhere',
                lineHeight: 1.7,
              }}
            >
              {
                run.steps.find((step) => step.role === TeamRole.Reviewer)
                  ?.output
              }
            </Typography>
          </Box>
        ))}
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
      const [brief, overview] = await Promise.all([
        service.brief(date, request.signal),
        service.get(request.signal),
      ]);
      if (this.isCurrent(generation)) {
        this.setState({ brief, overview });
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
            (this.state.overview?.company?.enabled ||
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

  private companyLabel(status: CompanyLoopStatus | undefined): string {
    switch (status) {
      case CompanyLoopStatus.Stopped:
        return 'Company stopped';
      case CompanyLoopStatus.WaitingForCodex:
        return 'Waiting for Codex sign-in';
      case CompanyLoopStatus.WaitingForPortfolio:
        return 'Waiting for verified portfolio';
      case CompanyLoopStatus.Running:
        return 'Team is working';
      case CompanyLoopStatus.Waiting:
        return 'Company is monitoring';
      case CompanyLoopStatus.Error:
        return 'Company needs attention';
      case undefined:
        return this.state.loading ? 'Checking company…' : 'Company unavailable';
    }
  }

  private isCurrent(generation: number): boolean {
    return this.mounted && generation === this.generation;
  }

  private configurationLabel(): string {
    if (!this.state.overview) {
      return this.state.loading
        ? 'Checking report service…'
        : 'Report service unavailable';
    }

    return this.state.overview.configured
      ? 'Report service configured'
      : 'Report service not configured';
  }

  private static today(): string {
    return new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Bangkok',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date());
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
}
