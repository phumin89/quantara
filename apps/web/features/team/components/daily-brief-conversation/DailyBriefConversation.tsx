import { Component, createRef, type ReactNode } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Chip,
  CircularProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import ForumOutlinedIcon from '@mui/icons-material/ForumOutlined';
import { TeamRole } from '../../contracts/TeamRole';
import { TeamPresentation } from '../../presentation/TeamPresentation';
import BriefMessage from '../brief-message/BriefMessage';
import type { DailyBriefConversationProps } from './DailyBriefConversationProps';

export default class DailyBriefConversation extends Component<
  DailyBriefConversationProps,
  object,
  boolean
> {
  private static readonly bottomThreshold = 48;

  private readonly viewport = createRef<HTMLDivElement>();

  public override componentDidMount(): void {
    this.scrollToLatest();
  }

  public override getSnapshotBeforeUpdate(
    previousProps: DailyBriefConversationProps,
  ): boolean {
    if (!this.props.active) {
      return false;
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

  public override componentDidUpdate(
    _previousProps: DailyBriefConversationProps,
    _previousState: Readonly<object>,
    followLatest?: boolean,
  ): void {
    if (followLatest) {
      this.scrollToLatest();
    }
  }

  public override render(): ReactNode {
    const { brief, loading } = this.props;
    const reports =
      brief?.runs.flatMap((run) => run.steps.filter((step) => step.output)) ??
      [];

    return (
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
          <Typography variant="caption">{reports.length} reports</Typography>
        </Box>
        <Box
          ref={this.viewport}
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
    );
  }

  private scrollToLatest(): void {
    const viewport = this.viewport.current;
    if (this.props.active && viewport !== null) {
      viewport.scrollTop = viewport.scrollHeight;
    }
  }

  private renderConversation(): ReactNode {
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
                    Why this trade · {run.assessment.positions.length} position
                    reviews
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
                    {new Date(run.assessment.expiresAt).toLocaleString(
                      'en-GB',
                      { timeZone: 'Asia/Bangkok' },
                    )}{' '}
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
                          {opportunity.symbol} · {opportunity.reason} ·
                          Reconsider {opportunity.reconsiderWhen}
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
                            ...TeamPresentation.statusStyle(step.status),
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
                          <BriefMessage output={step.output} />
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
}
