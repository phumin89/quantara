'use client';

import { PureComponent } from 'react';
import {
  Box,
  Button,
  ClickAwayListener,
  Stack,
  Tooltip,
  Typography,
} from '@mui/material';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import FormatTime from '../../utils/FormatTime';
import { ConnectionStatusPresenter } from '../../presentation/ConnectionStatusPresenter';
import type { ConnectionStatusProps } from './ConnectionStatusProps';
import type { ConnectionStatusState } from './ConnectionStatusState';

export default class ConnectionStatus extends PureComponent<
  ConnectionStatusProps,
  ConnectionStatusState
> {
  private statusDismissed = false;

  public override state: ConnectionStatusState = {
    statusOpen: false,
    statusPinned: false,
  };

  public override render(): React.ReactNode {
    const { data, error, refreshing, now } = this.props;
    const presenter = new ConnectionStatusPresenter(
      error,
      Boolean(data?.syncing),
      data?.updatedAt ?? null,
      now,
      data !== null,
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
          Saved source timestamps · Bangkok
        </Typography>
        <Typography
          component="span"
          sx={{ color: 'inherit', fontSize: 12 }}
        >
          Snapshot: {FormatTime.format(data?.updatedAt || null)}
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
        {refreshing && (
          <Typography
            component="span"
            sx={{ color: 'inherit', fontSize: 12 }}
          >
            Reading saved snapshot.
          </Typography>
        )}
        <Typography
          component="span"
          sx={{ color: 'inherit', fontSize: 12 }}
        >
          Stale after 2 minutes. Refresh reads saved data; it does not start a
          broker import.
        </Typography>
      </Stack>
    );

    return (
      <ClickAwayListener
        onClickAway={() => {
          this.dismissStatus();
        }}
      >
        <Tooltip
          arrow
          describeChild
          open={this.state.statusOpen}
          onOpen={() => {
            if (!this.statusDismissed) {
              this.setState({ statusOpen: true });
            }
          }}
          onClose={(event) => {
            if (event.type === 'keydown') {
              this.dismissStatus();
            } else if (
              event.type !== 'mouseleave' ||
              !this.state.statusPinned
            ) {
              this.setState({ statusOpen: false, statusPinned: false });
            }
          }}
          disableTouchListener
          title={details}
          slotProps={{ tooltip: { sx: { maxWidth: 240 } } }}
        >
          <Button
            aria-label={`Snapshot status: ${status.label}`}
            onMouseEnter={() => {
              this.statusDismissed = false;
            }}
            onFocus={() => {
              this.statusDismissed = false;
            }}
            onClick={() => {
              if (this.state.statusPinned) {
                this.dismissStatus();
              } else {
                this.statusDismissed = false;
                this.setState({ statusOpen: true, statusPinned: true });
              }
            }}
            onKeyDown={(event) => {
              if (event.key === 'Escape') {
                this.dismissStatus();
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
              borderRadius: 'var(--radius-control)',
            }}
          >
            <InfoOutlinedIcon
              aria-hidden="true"
              sx={{ fontSize: 18 }}
            />
            <Box
              component="span"
              sx={{
                bgcolor: status.background,
                borderRadius: 'var(--radius-tag)',
                px: 0.75,
                py: 0.25,
              }}
            >
              {status.label}
            </Box>
          </Button>
        </Tooltip>
      </ClickAwayListener>
    );
  }

  private dismissStatus(): void {
    this.statusDismissed = true;
    this.setState({ statusOpen: false, statusPinned: false });
  }
}
