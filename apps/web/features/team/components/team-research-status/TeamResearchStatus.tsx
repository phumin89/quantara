import { Component, type ReactNode } from 'react';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import StopOutlinedIcon from '@mui/icons-material/StopOutlined';
import PlayArrowOutlinedIcon from '@mui/icons-material/PlayArrowOutlined';
import PsychologyOutlinedIcon from '@mui/icons-material/PsychologyOutlined';
import { CompanyLoopStatus } from '../../contracts/CompanyLoopStatus';
import { TeamPresentation } from '../../presentation/TeamPresentation';
import type { TeamResearchStatusProps } from './TeamResearchStatusProps';

export default class TeamResearchStatus extends Component<TeamResearchStatusProps> {
  public override render(): ReactNode {
    const { company, controlling, onControl } = this.props;
    const pauseAction =
      company?.enabled || company?.status === CompanyLoopStatus.Error;
    const controlAction = pauseAction ? 'Pause' : 'Resume';

    return (
      <>
        <Box
          sx={{
            border: '1px solid var(--border-panel)',
            borderRadius: 'var(--radius-panel)',
            bgcolor: 'var(--surface-panel)',
            boxShadow: 'var(--shadow-panel)',
            p: 'var(--panel-padding)',
          }}
        >
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: { xs: 1, sm: 2 },
            }}
          >
            <Stack
              direction="row"
              spacing={1.5}
              sx={{ minWidth: 0, alignItems: 'center' }}
            >
              <Box
                aria-hidden="true"
                sx={{
                  display: { xs: 'none', sm: 'grid' },
                  placeItems: 'center',
                  width: 44,
                  height: 44,
                  flexShrink: 0,
                  borderRadius: 'var(--radius-control)',
                  bgcolor: 'var(--color-lilac)',
                  color: 'text.primary',
                }}
              >
                <PsychologyOutlinedIcon />
              </Box>
              <Box sx={{ minWidth: 0 }}>
                <Typography
                  variant="body2"
                  sx={{
                    fontSize: { xs: 14, sm: 18 },
                    lineHeight: 1.4,
                    fontWeight: 800,
                  }}
                >
                  {this.companyLabel(company?.status)}
                </Typography>
                <Typography
                  variant="caption"
                  color="text.secondary"
                  sx={{ fontSize: { xs: 12, sm: 13 }, lineHeight: 1.5 }}
                >
                  Read-only research
                </Typography>
              </Box>
            </Stack>
            <Button
              variant="outlined"
              color={pauseAction ? 'error' : 'primary'}
              startIcon={
                pauseAction ? <StopOutlinedIcon /> : <PlayArrowOutlinedIcon />
              }
              aria-label={`${controlAction} research`}
              aria-busy={controlling}
              loading={controlling}
              loadingPosition="start"
              disabled={!company || controlling}
              onClick={onControl}
              sx={{
                minHeight: 44,
                minWidth: { xs: 96, sm: 164 },
                flexShrink: 0,
                px: { xs: 1.25, sm: 2 },
                borderRadius: 'var(--radius-control)',
                bgcolor: 'var(--surface-raised)',
                fontWeight: 750,
                textTransform: 'none',
                '&.Mui-disabled': controlling
                  ? { color: 'text.secondary' }
                  : undefined,
                '& .MuiButton-loadingIndicator': { color: 'text.secondary' },
              }}
            >
              {controlling ? (
                'Updating…'
              ) : (
                <Box component="span">
                  {controlAction}
                  <Box
                    component="span"
                    sx={{ display: { xs: 'none', sm: 'inline' } }}
                  >
                    {' research'}
                  </Box>
                </Box>
              )}
            </Button>
          </Box>
          {(company?.status === CompanyLoopStatus.Waiting ||
            company?.status === CompanyLoopStatus.DeferredBudget) &&
            company.nextRunAt && (
              <Typography
                component="p"
                variant="caption"
                color="text.secondary"
                sx={{
                  mt: { xs: 0.5, sm: 1 },
                  fontSize: 12,
                  lineHeight: 1.5,
                }}
              >
                Next review {this.formatDate(company.nextRunAt)} Bangkok
              </Typography>
            )}
          <Box
            component="details"
            sx={{
              mt: { xs: 0.5, sm: 1 },
              borderTop: 1,
              borderColor: 'divider',
            }}
          >
            <Box
              component="summary"
              sx={{
                minHeight: 44,
                alignContent: 'center',
                cursor: 'pointer',
                fontSize: 14,
                fontWeight: 750,
                borderRadius: 'var(--radius-control)',
                '&:focus-visible': {
                  outline: '2px solid',
                  outlineColor: 'info.main',
                  outlineOffset: 3,
                },
              }}
            >
              Research details
            </Box>
            <Stack spacing={0.5}>
              <Typography
                variant="caption"
                color="text.secondary"
              >
                Market signals + hourly review
              </Typography>
              {company?.lastCompletedAt && (
                <Typography
                  variant="caption"
                  color="text.secondary"
                >
                  Last completed {this.formatDate(company.lastCompletedAt)}
                </Typography>
              )}
              {company?.activeRole && (
                <Typography
                  variant="caption"
                  color="text.secondary"
                >
                  Current role: {TeamPresentation.roleName(company.activeRole)}
                </Typography>
              )}
              {company?.nextRole && (
                <Typography
                  variant="caption"
                  color="text.secondary"
                >
                  Next eligible: {TeamPresentation.roleName(company.nextRole)}
                </Typography>
              )}
              {company?.budgetReason && (
                <Typography
                  variant="caption"
                  color="text.secondary"
                >
                  {company.budgetMode === 'boundedRequests'
                    ? 'Limited research calls: '
                    : ''}
                  {this.budgetLabel(company.budgetReason)}
                </Typography>
              )}
            </Stack>
          </Box>
        </Box>
        {company?.error && <Alert severity="warning">{company.error}</Alert>}
      </>
    );
  }

  private companyLabel(status: CompanyLoopStatus | undefined): string {
    switch (status) {
      case CompanyLoopStatus.Stopped:
        return 'AI research stopped';
      case CompanyLoopStatus.WaitingForCodex:
        return 'Waiting for Codex sign-in';
      case CompanyLoopStatus.WaitingForPortfolio:
        return 'Waiting for verified portfolio';
      case CompanyLoopStatus.Running:
        return 'Team is working';
      case CompanyLoopStatus.Waiting:
        return 'Company is monitoring';
      case CompanyLoopStatus.DeferredBudget:
        return 'Research budget reached';
      case CompanyLoopStatus.Error:
        return 'Company needs attention';
      case undefined:
        return this.props.loading ? 'Checking company…' : 'Company unavailable';
    }
  }

  private formatDate(value: string): string {
    return new Date(value).toLocaleString('en-GB', {
      timeZone: 'Asia/Bangkok',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private budgetLabel(reason: string): string {
    switch (reason) {
      case 'ordinary_research_limit':
        return 'Routine research slots are busy.';
      case 'position_review_limit':
        return 'Position review slot is busy.';
      case 'rolling_hour_limit':
        return 'Research slots are busy.';
      case 'future_claim':
        return 'Waiting for clock alignment.';
      case 'waiting_for_evidence':
        return 'Waiting for new evidence.';
      default:
        return reason;
    }
  }
}
