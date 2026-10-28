import { Component, createRef, type ReactNode } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Chip,
  CircularProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import ForumOutlinedIcon from '@mui/icons-material/ForumOutlined';
import ArrowDownwardOutlinedIcon from '@mui/icons-material/ArrowDownwardOutlined';
import { TeamRole } from '../../contracts/TeamRole';
import type { TeamRun } from '../../contracts/TeamRun';
import { TeamPresentation } from '../../presentation/TeamPresentation';
import BriefMessage from '../brief-message/BriefMessage';
import ReportTable from '../brief-message/ReportTable';
import type { DailyBriefConversationProps } from './DailyBriefConversationProps';
import type { DailyBriefConversationState } from './DailyBriefConversationState';

export default class DailyBriefConversation extends Component<
  DailyBriefConversationProps,
  DailyBriefConversationState,
  boolean
> {
  private static readonly bottomThreshold = 48;

  private readonly viewport = createRef<HTMLDivElement>();

  private anchorOpened = false;

  private readingPosition: number | null = null;

  private observedHash: string | null = null;

  public override state: DailyBriefConversationState = {
    hasNewMessages: false,
    selectedRunId: null,
  };

  public override componentDidMount(): void {
    if (this.props.archive) {
      window.addEventListener('hashchange', this.syncLocation);
      window.addEventListener('popstate', this.syncLocation);
      window.addEventListener('quantara:route-change', this.syncLocation);
    }

    this.syncSelectedRun(this.props.brief);
    this.scrollToLatest();
  }

  public override componentWillUnmount(): void {
    window.removeEventListener('hashchange', this.syncLocation);
    window.removeEventListener('popstate', this.syncLocation);
    window.removeEventListener('quantara:route-change', this.syncLocation);
  }

  private readonly syncLocation = (): void => {
    this.syncSelectedRun(this.props.brief);
  };

  public override componentDidUpdate(
    previousProps: DailyBriefConversationProps,
    _previousState: Readonly<DailyBriefConversationState>,
    followLatest?: boolean,
  ): void {
    if (this.props.archive) {
      this.syncSelectedRun(this.props.brief);
    }

    if (followLatest) {
      this.scrollToLatest();
    }

    if (this.props.embedded && previousProps.brief !== this.props.brief) {
      const before = previousProps.brief?.runs
        .map((run) => run.id + run.updatedAt)
        .join('|');
      const after = this.props.brief?.runs
        .map((run) => run.id + run.updatedAt)
        .join('|');
      if (before !== after) {
        this.setState({ hasNewMessages: this.readingPosition !== null });
      }
    }
  }

  public override getSnapshotBeforeUpdate(
    previousProps: DailyBriefConversationProps,
  ): boolean {
    if (!this.props.active) {
      return false;
    }

    if (this.props.preservePosition) {
      if (previousProps.date !== this.props.date) {
        this.readingPosition = null;
        this.anchorOpened = false;
      } else if (
        previousProps.active &&
        this.viewport.current &&
        previousProps.brief?.runs.length
      ) {
        this.readingPosition = this.nearBottom()
          ? null
          : this.viewport.current.scrollTop;
      }

      return true;
    }

    if (!previousProps.active || previousProps.date !== this.props.date) {
      return true;
    }

    const viewport = this.viewport.current;

    return (
      viewport !== null &&
      viewport.scrollHeight - viewport.clientHeight - viewport.scrollTop <=
        DailyBriefConversation.bottomThreshold
    );
  }

  public override render(): ReactNode {
    const { brief, loading } = this.props;
    let viewportHeight: string | { xs: number; md: number } = {
      xs: 420,
      md: 500,
    };
    if (this.props.embedded) {
      viewportHeight = 'auto';
    }

    if (this.props.archive) {
      viewportHeight = 'clamp(320px, 62dvh, 720px)';
    }

    const reports = brief?.runs ?? [];
    const archiveRuns = [...reports].sort(
      (left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt),
    );
    const selectedArchiveRun = archiveRuns.find(
      (run) => run.id === this.state.selectedRunId,
    );

    return (
      <Paper
        variant="outlined"
        sx={{
          borderRadius: this.props.embedded ? 0 : 'var(--radius-panel)',
          border: this.props.embedded ? 0 : '1px solid var(--border-panel)',
          bgcolor: 'var(--surface-panel)',
          boxShadow: this.props.embedded ? 'none' : 'var(--shadow-panel)',
          overflow: 'hidden',
          height: this.props.embedded ? '100%' : undefined,
          display: 'flex',
          flexDirection: 'column',
          '& summary': {
            minHeight: 44,
            alignContent: 'center',
            borderRadius: 'var(--radius-control)',
            '&:focus-visible': {
              outline: '2px solid',
              outlineColor: 'info.main',
              outlineOffset: 2,
            },
          },
        }}
      >
        {!this.props.embedded && (
          <Box
            sx={{
              bgcolor: this.props.archive
                ? 'var(--surface-header)'
                : 'primary.main',
              color: this.props.archive
                ? 'text.primary'
                : 'primary.contrastText',
              px: 'var(--panel-padding)',
              pt: 'var(--panel-padding)',
              pb: 1,
              display: 'flex',
              alignItems: 'center',
              gap: 1,
            }}
          >
            {!this.props.archive && <ForumOutlinedIcon fontSize="small" />}
            <Typography
              component="h2"
              id={this.props.archive ? 'daily-brief-report-heading' : undefined}
              sx={{
                fontSize: this.props.archive ? { xs: 18, sm: 20 } : 16,
                fontWeight: 800,
                flex: 1,
                minWidth: 0,
              }}
            >
              {this.props.archive ? 'Daily reports' : 'Team conversation'}
            </Typography>
            {brief && (
              <Typography
                variant="caption"
                sx={{
                  px: { xs: 0.75, sm: 1.25 },
                  py: 0.5,
                  borderRadius: 'var(--radius-tag)',
                  bgcolor: this.props.archive
                    ? 'var(--color-lilac)'
                    : 'var(--surface-soft)',
                  color: 'text.primary',
                  fontSize: 12,
                  fontWeight: 700,
                  textAlign: 'right',
                }}
              >
                {this.props.archive ? this.props.date + ' · ' : ''}
                {reports.length} {reports.length === 1 ? 'report' : 'reports'}
              </Typography>
            )}
          </Box>
        )}
        {this.props.archive && archiveRuns.length > 0 && (
          <Box
            sx={{
              px: 'var(--panel-padding)',
              pb: 1,
              bgcolor: 'var(--surface-header)',
              '& select:focus-visible': {
                outline: '2px solid',
                outlineColor: 'info.main',
                outlineOffset: 3,
              },
            }}
          >
            <select
              id="daily-brief-report-select"
              aria-labelledby="daily-brief-report-heading"
              value={selectedArchiveRun?.id ?? archiveRuns[0]?.id ?? ''}
              onChange={(event) => {
                this.selectRun(event.target.value);
              }}
              style={{
                width: '100%',
                minWidth: 0,
                minHeight: 44,
                padding: '10px 14px',
                border: '1px solid var(--border-panel)',
                borderRadius: 'var(--radius-control)',
                background: 'var(--surface-raised)',
                color: 'inherit',
                font: 'inherit',
                fontSize: 16,
              }}
            >
              {archiveRuns.map((run) => (
                <option
                  key={run.id}
                  value={run.id}
                >
                  {new Date(run.updatedAt).toLocaleTimeString('en-GB', {
                    timeZone: 'Asia/Bangkok',
                    hour: '2-digit',
                    minute: '2-digit',
                  })}{' '}
                  · updated ·{' '}
                  {TeamPresentation.roleName(
                    run.steps[0]?.role ?? TeamRole.Ceo,
                  )}{' '}
                  · {run.status}
                </option>
              ))}
            </select>
            {selectedArchiveRun &&
              selectedArchiveRun.id !== archiveRuns[0]?.id && (
                <Button
                  size="small"
                  sx={{ mt: 0.5 }}
                  onClick={() => {
                    const latest = archiveRuns[0];
                    if (latest) {
                      this.selectRun(latest.id);
                    }
                  }}
                >
                  Latest report
                </Button>
              )}
          </Box>
        )}
        <Box
          ref={this.viewport}
          role="region"
          aria-label={
            this.props.archive ? 'Research reports' : 'Daily conversation'
          }
          tabIndex={0}
          aria-busy={loading}
          onScroll={() => {
            if (
              this.props.preservePosition &&
              this.props.active &&
              this.props.brief?.runs.length &&
              this.viewport.current
            ) {
              this.readingPosition = this.nearBottom()
                ? null
                : this.viewport.current.scrollTop;
              if (this.readingPosition === null && this.state.hasNewMessages) {
                this.setState({ hasNewMessages: false });
              }
            }
          }}
          sx={{
            bgcolor: 'var(--surface-raised)',
            height: this.props.archive ? 'auto' : viewportHeight,
            maxHeight: this.props.archive ? viewportHeight : undefined,
            flex: this.props.embedded ? 1 : undefined,
            minHeight: 0,
            overflowY: 'auto',
            scrollbarGutter: 'stable',
            borderTop: this.props.archive ? 1 : 0,
            borderColor: 'divider',
            p: this.props.embedded ? 2 : 'var(--panel-padding)',
            '&:focus-visible': {
              outline: '2px solid',
              outlineColor: 'info.main',
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
            this.renderConversation(selectedArchiveRun?.id)
          )}
        </Box>
        {this.props.embedded && this.state.hasNewMessages && (
          <Button
            size="small"
            endIcon={<ArrowDownwardOutlinedIcon />}
            onClick={() => {
              this.readingPosition = null;
              this.scrollToLatest();
              this.setState({ hasNewMessages: false });
            }}
            sx={{
              minHeight: 44,
              flexShrink: 0,
              bgcolor: 'var(--color-mint)',
              borderRadius: 0,
              fontWeight: 750,
            }}
          >
            New messages
          </Button>
        )}
        {!this.props.embedded && !this.props.archive && (
          <Box sx={{ px: 2, py: 1, borderTop: 1, borderColor: 'divider' }}>
            <Typography
              variant="caption"
              color="text.secondary"
            >
              Recorded team reports · Dates in Bangkok time
            </Typography>
          </Box>
        )}
      </Paper>
    );
  }

  private scrollToLatest(): void {
    const viewport = this.viewport.current;
    if (this.props.archive) {
      const anchor = window.location.hash.slice(1);
      const report = /^run-[0-9a-f-]{36}$/i.test(anchor)
        ? document.getElementById(anchor)
        : null;
      if (!this.anchorOpened && report && viewport?.contains(report)) {
        viewport.scrollTop +=
          report.getBoundingClientRect().top -
          viewport.getBoundingClientRect().top;
        this.anchorOpened = true;
      }

      return;
    }

    if (this.props.active && viewport !== null) {
      if (this.props.preservePosition && this.readingPosition !== null) {
        viewport.scrollTop = this.readingPosition;

        return;
      }

      const anchor = window.location.hash.slice(1);
      const report = /^run-[0-9a-f-]{36}$/i.test(anchor)
        ? document.getElementById(
            `${this.props.idPrefix ?? 'run-'}${anchor.slice(4)}`,
          )
        : null;
      if (!this.anchorOpened && report && viewport.contains(report)) {
        viewport.scrollTop +=
          report.getBoundingClientRect().top -
          viewport.getBoundingClientRect().top;
        this.anchorOpened = true;

        return;
      }

      viewport.scrollTop = viewport.scrollHeight;
    }
  }

  private nearBottom(): boolean {
    const viewport = this.viewport.current;

    return (
      viewport !== null &&
      viewport.scrollHeight - viewport.clientHeight - viewport.scrollTop <=
        DailyBriefConversation.bottomThreshold
    );
  }

  private renderConversation(selectedArchiveRunId?: string): ReactNode {
    const { brief, error, date } = this.props;
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
          <ForumOutlinedIcon
            sx={{ fontSize: 40, color: 'text.secondary', mb: 1 }}
          />
          <Typography sx={{ fontWeight: 800 }}>
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

    const runs = this.props.archive
      ? brief.runs.filter((run) => run.id === selectedArchiveRunId)
      : brief.runs;

    return (
      <Stack spacing={3}>
        {(this.props.embedded
          ? [...runs].sort(
              (left, right) =>
                Date.parse(left.createdAt) - Date.parse(right.createdAt),
            )
          : runs
        ).map((run) => (
          <Stack
            key={run.id}
            id={`${this.props.idPrefix ?? 'run-'}${run.id}`}
            spacing={2}
          >
            {this.props.archive &&
              Array.from(
                new Set(
                  [run.error, ...run.steps.map((step) => step.error)].filter(
                    (error): error is string => !!error,
                  ),
                ),
              ).map((error) => (
                <Alert
                  key={error}
                  severity="error"
                  sx={{ mt: 1, overflowWrap: 'anywhere' }}
                >
                  {error}
                </Alert>
              ))}
            {!this.props.archive && this.renderRunContext(run)}
            {run.steps
              .filter((step) => step.output || step.error)
              .map((step) => {
                const member =
                  TeamPresentation.members.find(
                    (item) => item.role === step.role,
                  ) ??
                  TeamPresentation.legacyMembers.find(
                    (item) => item.role === step.role,
                  );

                return (
                  <Stack
                    key={step.role}
                    direction="row"
                    spacing={1.5}
                    sx={{ alignItems: 'flex-start' }}
                  >
                    {!this.props.archive && (
                      <Avatar
                        sx={{
                          width: 32,
                          height: 32,
                          fontSize: 13,
                          fontWeight: 700,
                          borderRadius: 'var(--radius-tag)',
                          bgcolor: member?.background,
                          color: member?.foreground,
                        }}
                      >
                        {member?.initials}
                      </Avatar>
                    )}
                    <Box sx={{ minWidth: 0, flex: 1 }}>
                      <Stack
                        direction="row"
                        spacing={1}
                        sx={{
                          mb: 0.75,
                          alignItems: 'center',
                          flexWrap: 'wrap',
                          gap: 0.5,
                        }}
                      >
                        <Typography
                          component={this.props.archive ? 'h3' : 'p'}
                          sx={{
                            fontSize: this.props.archive ? 18 : 14,
                            fontWeight: 800,
                          }}
                        >
                          {member?.name}
                        </Typography>
                        {this.props.embedded && (
                          <Typography
                            component="time"
                            dateTime={
                              step.completedAt ??
                              step.startedAt ??
                              run.updatedAt
                            }
                            variant="caption"
                            color="text.secondary"
                          >
                            {step.completedAt || step.startedAt
                              ? ''
                              : 'Run updated '}
                            {new Date(
                              step.completedAt ??
                                step.startedAt ??
                                run.updatedAt,
                            ).toLocaleTimeString('en-GB', {
                              timeZone: 'Asia/Bangkok',
                              hour: '2-digit',
                              minute: '2-digit',
                            })}
                          </Typography>
                        )}
                        <Chip
                          size="small"
                          label={step.status}
                          sx={{
                            height: 24,
                            fontSize: 12,
                            borderRadius: 'var(--radius-tag)',
                            textTransform: 'capitalize',
                            ...TeamPresentation.statusStyle(step.status),
                          }}
                        />
                      </Stack>
                      {step.output && (
                        <Box
                          sx={{
                            bgcolor: 'transparent',
                            border:
                              this.props.archive || this.props.embedded
                                ? 0
                                : '1px solid var(--border-panel)',
                            borderRadius: 'var(--radius-control)',
                            p:
                              this.props.archive || this.props.embedded
                                ? 0
                                : 1.5,
                            maxWidth: '75ch',
                          }}
                        >
                          <BriefMessage
                            output={step.output}
                            role={step.role}
                            idPrefix={`${this.props.idPrefix ?? 'run-'}${run.id}-${step.role}-`}
                          />
                        </Box>
                      )}
                      {step.role === TeamRole.Reviewer && run.lesson && (
                        <details style={{ marginTop: 12 }}>
                          <Typography
                            component="summary"
                            variant="body2"
                            sx={{ cursor: 'pointer', fontWeight: 600 }}
                          >
                            Learning notes
                          </Typography>
                          <Stack
                            spacing={0.5}
                            sx={{ mt: 1, overflowWrap: 'anywhere' }}
                          >
                            <Typography variant="body2">
                              <Box
                                component="span"
                                sx={{ fontWeight: 700 }}
                              >
                                Lesson:
                              </Box>{' '}
                              {run.lesson.lesson}
                            </Typography>
                            <Typography variant="body2">
                              <Box
                                component="span"
                                sx={{ fontWeight: 700 }}
                              >
                                Evidence:
                              </Box>{' '}
                              {run.lesson.evidence}
                            </Typography>
                            <Typography variant="body2">
                              <Box
                                component="span"
                                sx={{ fontWeight: 700 }}
                              >
                                Next test:
                              </Box>{' '}
                              {run.lesson.nextTest}
                            </Typography>
                          </Stack>
                        </details>
                      )}
                      {step.error && !this.props.archive && (
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
            {this.props.archive && this.renderRunContext(run)}
            {run.error &&
              !this.props.archive &&
              !run.steps.some((step) => step.error === run.error) && (
                <Alert severity="error">{run.error}</Alert>
              )}
          </Stack>
        ))}
      </Stack>
    );
  }

  private renderRunContext(run: TeamRun): ReactNode {
    return (
      <Box
        role={this.props.archive ? 'group' : undefined}
        aria-label={this.props.archive ? 'Report details' : undefined}
        sx={{
          pt: this.props.archive ? 1 : 0,
          pb: 1,
          borderTop: this.props.archive ? 1 : 0,
          borderBottom: this.props.archive ? 0 : 1,
          borderColor: 'divider',
        }}
      >
        {!this.props.archive && (
          <Typography
            variant="caption"
            color="text.secondary"
          >
            Research snapshot updated{' '}
            {new Intl.DateTimeFormat('en-GB', {
              timeZone: 'Asia/Bangkok',
              hour: '2-digit',
              minute: '2-digit',
            }).format(new Date(run.updatedAt))}
          </Typography>
        )}
        {this.props.archive && (
          <Box
            component="details"
            sx={{ mt: 1 }}
          >
            <Box
              component="summary"
              sx={{ minHeight: 44, cursor: 'pointer', fontWeight: 600 }}
            >
              Run status and evidence
            </Box>
            <ReportTable aria-label="Recorded run status">
              <thead>
                <tr>
                  <th scope="col">Role</th>
                  <th scope="col">Status</th>
                  <th scope="col">Recorded reason</th>
                </tr>
              </thead>
              <tbody>
                {run.steps.map((step) => (
                  <tr key={step.role}>
                    <td>{TeamPresentation.roleName(step.role)}</td>
                    <td>{step.status}</td>
                    <td>{step.error ?? 'Not recorded separately.'}</td>
                  </tr>
                ))}
              </tbody>
            </ReportTable>
          </Box>
        )}
        {
          <details>
            <Typography
              component="summary"
              variant="body2"
              sx={{
                cursor: 'pointer',
                fontWeight: 600,
                overflowWrap: 'anywhere',
                mt: 0.5,
              }}
            >
              Research brief
            </Typography>
            <Typography
              variant="body2"
              sx={{ mt: 1, overflowWrap: 'anywhere' }}
            >
              {run.objective}
            </Typography>
          </details>
        }
        {run.assessment && run.assessment.runId === run.id ? (
          <Box
            component="details"
            sx={{ mt: 1 }}
          >
            <Box
              component="summary"
              sx={{
                cursor: 'pointer',
                minHeight: 44,
                alignContent: 'center',
                fontWeight: 600,
              }}
            >
              Assessment &amp; evidence · {run.assessment.positions.length}{' '}
              position reviews
            </Box>
            {run.assessment.positions.map((position) => (
              <Box
                key={position.symbol}
                sx={{ mt: 1 }}
              >
                <Typography
                  variant="body2"
                  sx={{ fontWeight: 600 }}
                >
                  {position.symbol} · {position.action}
                </Typography>
                <Typography variant="body2">
                  {position.thesis} {position.reason}
                </Typography>
                {position.missingFields.length > 0 && (
                  <Typography
                    variant="caption"
                    color="warning.main"
                  >
                    Missing: {position.missingFields.join(', ')}
                  </Typography>
                )}
              </Box>
            ))}
            <Typography
              variant="caption"
              color="text.secondary"
            >
              Review{' '}
              {Date.parse(run.assessment.expiresAt) <= Date.now()
                ? 'expired'
                : 'expires'}
              :{' '}
              {new Date(run.assessment.expiresAt).toLocaleString('en-GB', {
                timeZone: 'Asia/Bangkok',
              })}{' '}
              Bangkok
            </Typography>
            {run.assessment.opportunities.length > 0 && (
              <Box
                component="details"
                sx={{ mt: 0.5 }}
              >
                <Box
                  component="summary"
                  sx={{
                    cursor: 'pointer',
                    minHeight: 44,
                    alignContent: 'center',
                    fontWeight: 600,
                  }}
                >
                  Opportunities
                </Box>
                {run.assessment.opportunities.map((opportunity) => (
                  <Typography
                    key={opportunity.symbol}
                    variant="body2"
                  >
                    {opportunity.symbol} · {opportunity.reason} · Reconsider{' '}
                    {opportunity.reconsiderWhen}
                  </Typography>
                ))}
              </Box>
            )}
            {run.assessment.evidenceIds.length > 0 && (
              <Stack
                spacing={0.25}
                sx={{ mt: 0.5 }}
              >
                {run.assessment.evidenceIds.map((id) => (
                  <Typography
                    key={id}
                    variant="caption"
                    color="text.secondary"
                  >
                    Evidence: {id}
                  </Typography>
                ))}
              </Stack>
            )}
          </Box>
        ) : (
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: 'block', mt: 1 }}
          >
            Trade assessment: Unknown for this run.
          </Typography>
        )}
      </Box>
    );
  }

  private syncSelectedRun(brief: DailyBriefConversationProps['brief']): void {
    if (!this.props.archive || !brief?.runs.length) {
      return;
    }

    const runs = [...brief.runs].sort(
      (left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt),
    );
    const hash = window.location.hash.slice(1);
    const hashChanged = this.observedHash !== hash;
    const hashRunId = /^run-[0-9a-f-]{36}$/i.test(hash) ? hash.slice(4) : null;
    const selectedRunId = this.state.selectedRunId;
    const linkedRun = runs.find((run) => run.id === hashRunId);
    if (!hashRunId || linkedRun) {
      this.observedHash = hash;
    }

    const retainedRun = runs.find((run) => run.id === selectedRunId);
    const nextRunId =
      (hashChanged ? linkedRun?.id : null) ??
      retainedRun?.id ??
      linkedRun?.id ??
      runs[0]?.id ??
      null;

    if (nextRunId !== selectedRunId) {
      this.anchorOpened = false;
      this.setState({ selectedRunId: nextRunId }, () => {
        if (this.viewport.current) {
          this.viewport.current.scrollTop = 0;
        }

        this.scrollToLatest();
      });
    } else if (hashChanged && linkedRun) {
      this.anchorOpened = false;
      this.scrollToLatest();
    }
  }

  private selectRun(id: string): void {
    const location = new URL(window.location.href);
    location.searchParams.set('date', this.props.date);
    location.hash = 'run-' + id;
    window.history.replaceState(window.history.state, '', location.href);
    this.observedHash = window.location.hash.slice(1);
    this.anchorOpened = true;
    this.setState({ selectedRunId: id }, () => {
      if (this.viewport.current) {
        this.viewport.current.scrollTop = 0;
      }
    });
  }
}
