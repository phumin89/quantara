import { Component, type ReactNode } from 'react';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import StopOutlinedIcon from '@mui/icons-material/StopOutlined';
import PlayArrowOutlinedIcon from '@mui/icons-material/PlayArrowOutlined';
import { CompanyLoopStatus } from '../../contracts/CompanyLoopStatus';
import { TeamPresentation } from '../../presentation/TeamPresentation';
import type { TeamResearchStatusProps } from './TeamResearchStatusProps';

export default class TeamResearchStatus extends Component<TeamResearchStatusProps> {
  public override render(): ReactNode {
    const { company, controlling, onControl } = this.props;

    return (
      <>
        <Box
          sx={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 1.5,
          }}
        >
          <Stack spacing={0.25}>
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
              Read-only research · Market signals + hourly review
            </Typography>
            {(company?.status === CompanyLoopStatus.Waiting ||
              company?.status === CompanyLoopStatus.DeferredBudget) &&
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
            onClick={onControl}
            sx={{ minHeight: 44, textTransform: 'none' }}
          >
            {company?.enabled || company?.status === CompanyLoopStatus.Error
              ? 'Pause research'
              : 'Resume research'}
          </Button>
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
