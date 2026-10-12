'use client';

import { PureComponent } from 'react';
import { Box, Button, Stack, Tooltip, Typography } from '@mui/material';
import FormatTime from '../../utils/FormatTime';
import { ConnectionStatusPresenter } from '../../presentation/ConnectionStatusPresenter';
import type { ConnectionStatusProps } from './ConnectionStatusProps';
import type { ConnectionStatusState } from './ConnectionStatusState';

export default class ConnectionStatus extends PureComponent<
  ConnectionStatusProps,
  ConnectionStatusState
> {
  public override state: ConnectionStatusState = { statusOpen: false };

  public override render(): React.ReactNode {
    const { data, error, refreshing } = this.props;
    const presenter = new ConnectionStatusPresenter(
      error,
      refreshing || Boolean(data?.syncing),
      data?.updatedAt ?? null,
    );
    const status = presenter.appearance;
    const details = (
      <Stack
        spacing={0.5}
        sx={{ fontSize: 12 }}
      >
        <Typography
          component="span"
          sx={{ color: 'inherit', fontSize: 12, fontWeight: 600 }}
        >
          Last successful updates
        </Typography>
        <Typography
          component="span"
          sx={{ color: 'inherit', fontSize: 12 }}
        >
          Wallet: {FormatTime.format(data?.updatedAt || null)}
        </Typography>
        <Typography
          component="span"
          sx={{ color: 'inherit', fontSize: 12 }}
        >
          Prices: {FormatTime.format(data?.priceUpdatedAt || null)}
        </Typography>
        <Typography
          component="span"
          sx={{ color: 'inherit', fontSize: 12 }}
        >
          History: {FormatTime.format(data?.historyUpdatedAt || null)}
        </Typography>
        {data?.syncing && data.totalSymbols > 0 && (
          <Typography
            component="span"
            sx={{ color: 'inherit', fontSize: 12 }}
          >
            Progress: {data.completedSymbols}/{data.totalSymbols} symbols
          </Typography>
        )}
      </Stack>
    );

    return (
      <Tooltip
        arrow
        open={this.state.statusOpen}
        disableHoverListener
        disableFocusListener
        disableTouchListener
        disableInteractive
        title={details}
        slotProps={{ tooltip: { sx: { maxWidth: 240 } } }}
      >
        <Button
          aria-label={`Bitkub status: ${status.label}`}
          onClick={() => {
            this.setState({ statusOpen: true });
          }}
          onMouseEnter={() => {
            this.setState({ statusOpen: true });
          }}
          onMouseLeave={() => {
            this.setState({ statusOpen: false });
          }}
          onFocus={() => {
            this.setState({ statusOpen: true });
          }}
          onBlur={() => {
            this.setState({ statusOpen: false });
          }}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              this.setState({ statusOpen: false });
            }
          }}
          sx={{
            minHeight: 44,
            minWidth: 44,
            p: 0.5,
            gap: 0.75,
            color: status.color,
            textTransform: 'none',
            fontSize: 12,
            fontWeight: 600,
            borderRadius: '6px',
          }}
        >
          <Box
            aria-hidden="true"
            sx={{
              width: 8,
              height: 8,
              borderRadius: '50%',
              bgcolor: status.color,
            }}
          />
          <Box
            component="span"
            sx={{
              bgcolor: status.background,
              borderRadius: '4px',
              px: 0.75,
              py: 0.25,
            }}
          >
            {status.label}
          </Box>
        </Button>
      </Tooltip>
    );
  }
}
