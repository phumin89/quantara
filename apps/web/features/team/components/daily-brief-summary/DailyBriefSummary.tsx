import { Component, type ReactNode } from 'react';
import { Box, Paper, Stack, Typography } from '@mui/material';
import AssignmentOutlinedIcon from '@mui/icons-material/AssignmentOutlined';
import { TeamRole } from '../../contracts/TeamRole';
import BriefMessage from '../brief-message/BriefMessage';
import type { DailyBriefSummaryProps } from './DailyBriefSummaryProps';

export default class DailyBriefSummary extends Component<DailyBriefSummaryProps> {
  public override render(): ReactNode {
    return (
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
    );
  }

  private renderSummary(): ReactNode {
    const summaries =
      this.props.brief?.runs.filter((run) =>
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
          {this.props.error
            ? 'Summary unavailable until records can be loaded.'
            : 'No reviewer summary recorded for this day.'}
        </Typography>
      );
    }

    const latest = summaries.at(-1);
    const previous = summaries.slice(0, -1).reverse();

    return (
      <Stack spacing={2}>
        {latest && (
          <Box key={latest.id}>
            <Typography
              variant="caption"
              color="text.secondary"
            >
              Latest Reviewer report
            </Typography>
            <Box sx={{ mt: 1 }}>
              <BriefMessage
                output={
                  latest.steps.find((step) => step.role === TeamRole.Reviewer)
                    ?.output ?? ''
                }
              />
            </Box>
          </Box>
        )}
        {previous.length > 0 && (
          <details>
            <Typography
              component="summary"
              variant="body2"
              sx={{ cursor: 'pointer', fontWeight: 600 }}
            >
              Previous summaries ({previous.length})
            </Typography>
            <Stack
              spacing={2}
              sx={{ mt: 1.5 }}
            >
              {previous.map((run) => (
                <Box key={run.id}>
                  <Typography
                    variant="caption"
                    color="text.secondary"
                  >
                    Reviewer report
                  </Typography>
                  <Box sx={{ mt: 1 }}>
                    <BriefMessage
                      output={
                        run.steps.find(
                          (step) => step.role === TeamRole.Reviewer,
                        )?.output ?? ''
                      }
                    />
                  </Box>
                </Box>
              ))}
            </Stack>
          </details>
        )}
      </Stack>
    );
  }
}
