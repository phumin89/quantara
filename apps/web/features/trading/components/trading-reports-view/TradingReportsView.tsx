import { Component, type ReactNode } from 'react';
import { Stack, Typography } from '@mui/material';
import TradingFormatting from '../../presentation/TradingFormatting';
import type { TradingReportsViewProps } from './TradingReportsViewProps';

export default class TradingReportsView extends Component<TradingReportsViewProps> {
  public override render(): ReactNode {
    const overview = this.props.overview;
    const reports = overview.reports
      .filter((report) => report.date === this.props.date)
      .sort((left, right) => right.revision - left.revision);
    const report = reports.at(0);

    return (
      <Stack spacing={1}>
        <Typography
          component="h3"
          sx={{ fontSize: 14, fontWeight: 700 }}
        >
          Trading daily report · {this.props.date || 'Select a date'}
        </Typography>
        <Typography
          variant="caption"
          color="text.secondary"
        >
          23:59 Asia/Bangkok · Corrected reports show the latest revision.
        </Typography>
        {report ? (
          <>
            <Typography variant="body2">
              {report.mode} · Revision {report.revision} · Delivery{' '}
              {report.status === 'local' ? 'Saved in app' : report.status}
            </Typography>
            <Typography variant="body2">
              Starting equity{' '}
              {TradingFormatting.money(report.startingEquityThb)} · Ending
              equity {TradingFormatting.money(report.endingEquityThb)} · Net
              P&amp;L {TradingFormatting.money(report.realizedNetThb)}
            </Typography>
            <Typography
              lang="th"
              variant="body2"
              sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}
            >
              {report.text}
            </Typography>
          </>
        ) : (
          <Typography
            variant="body2"
            color="text.secondary"
          >
            No trading report for this date.
          </Typography>
        )}
      </Stack>
    );
  }
}
