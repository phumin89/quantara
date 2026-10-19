import { Component, type ReactNode } from 'react';
import { Box, Chip, Stack, Typography } from '@mui/material';
import TradingFormatting from '../../presentation/TradingFormatting';
import type { TradingRealtimeViewProps } from './TradingRealtimeViewProps';

export default class TradingRealtimeView extends Component<TradingRealtimeViewProps> {
  public override render(): ReactNode {
    const overview = this.props.overview;
    const realtime = overview.realtime;
    if (!realtime) {
      return null;
    }

    const eventAgeMs = realtime.lastEventAt
      ? Date.now() - Date.parse(realtime.lastEventAt)
      : NaN;
    const fresh =
      Number.isFinite(eventAgeMs) &&
      eventAgeMs >= -5_000 &&
      eventAgeMs <= 45_000;
    const connected = realtime.connected && fresh;
    const age = realtime.lastEventAt
      ? TradingFormatting.age(realtime.lastEventAt)
      : 'Unknown';

    return (
      <Box>
        <Stack
          direction="row"
          spacing={1}
          sx={{ alignItems: 'center', flexWrap: 'wrap' }}
        >
          <Chip
            size="small"
            color={connected ? 'success' : 'warning'}
            label={`${connected ? 'Connected' : 'Disconnected'} · ${realtime.watchedSymbols.length.toString()} watched`}
          />
          {!fresh && realtime.lastEventAt && (
            <Typography
              variant="caption"
              color="text.secondary"
            >
              Last event {age}
            </Typography>
          )}
        </Stack>
        <Box
          component="details"
          sx={{
            mt: 0.5,
            '& > summary': {
              cursor: 'pointer',
              minHeight: 36,
              alignContent: 'center',
              fontSize: 13,
              color: 'text.secondary',
            },
          }}
        >
          <Box component="summary">Feed diagnostics</Box>
          <Typography
            variant="caption"
            component="div"
          >
            Watched: {realtime.watchedSymbols.join(', ') || 'None'} · Dirty:{' '}
            {realtime.dirtySymbols.join(', ') || 'None'}
          </Typography>
          <Typography
            variant="caption"
            component="div"
          >
            Coalesced: {realtime.coalesced} · Dropped: {realtime.dropped}
          </Typography>
          <Typography
            variant="caption"
            component="div"
          >
            Received → decision p50/p95:{' '}
            {TradingFormatting.milliseconds(realtime.decisionP50Ms)} /{' '}
            {TradingFormatting.milliseconds(realtime.decisionP95Ms)}
          </Typography>
          <Typography
            variant="caption"
            component="div"
          >
            Decision → submit:{' '}
            {TradingFormatting.milliseconds(realtime.lastSubmitLatencyMs)}
          </Typography>
          <Typography
            variant="caption"
            component="div"
          >
            Last exit evaluation:{' '}
            {TradingFormatting.timeWithAge(realtime.lastExitEvaluationAt)}
          </Typography>
          <Typography
            variant="caption"
            component="div"
          >
            Last reconciliation:{' '}
            {TradingFormatting.timeWithAge(realtime.lastReconciliationAt)}
          </Typography>
        </Box>
      </Box>
    );
  }
}
