'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Drawer,
  IconButton,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import CloseOutlinedIcon from '@mui/icons-material/CloseOutlined';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import ShowChartOutlinedIcon from '@mui/icons-material/ShowChartOutlined';
import { TradingServiceContext } from '../../context/TradingServiceContext';
import type { TradingOverview } from '../../contracts/TradingOverview';
import { tradingRulesSchema } from '../../contracts/TradingRules';
import type { TradingPanelProps } from './TradingPanelProps';
import type { TradingPanelState } from './TradingPanelState';

export default class TradingPanel extends Component<
  TradingPanelProps,
  TradingPanelState
> {
  public static override readonly contextType = TradingServiceContext.value;

  private static readonly fields = [
    {
      key: 'allocatedThb',
      label: 'Allocated capital (THB)',
      min: 0.01,
      max: undefined,
      step: 'any',
    },
    {
      key: 'perOrderThb',
      label: 'Per-order amount (THB)',
      min: 0.01,
      max: undefined,
      step: 'any',
    },
    {
      key: 'maxPositions',
      label: 'Maximum positions',
      min: 1,
      max: undefined,
      step: '1',
    },
    {
      key: 'dailyLossThb',
      label: 'Daily loss cap (THB)',
      min: 0.01,
      max: undefined,
      step: 'any',
    },
    {
      key: 'maxSpreadBps',
      label: 'Maximum spread (bps)',
      min: 0,
      max: 1000,
      step: 'any',
    },
    {
      key: 'slippageBps',
      label: 'Slippage cap (bps)',
      min: 0,
      max: 1000,
      step: 'any',
    },
    {
      key: 'maxDataAgeSeconds',
      label: 'Maximum data age (seconds)',
      min: 10,
      max: 300,
      step: '1',
    },
    {
      key: 'takeProfitBps',
      label: 'Take profit (bps)',
      min: 1,
      max: 10000,
      step: 'any',
    },
    {
      key: 'stopLossBps',
      label: 'Stop loss (bps)',
      min: 1,
      max: 10000,
      step: 'any',
    },
    {
      key: 'trailingStopBps',
      label: 'Trailing stop (bps)',
      min: 1,
      max: 10000,
      step: 'any',
    },
    {
      key: 'timeExitMinutes',
      label: 'Time exit (minutes)',
      min: 1,
      max: 1440,
      step: '1',
    },
    {
      key: 'cooldownMinutes',
      label: 'Cooldown (minutes)',
      min: 0,
      max: undefined,
      step: '1',
    },
    {
      key: 'orderLifetimeMinutes',
      label: 'Order lifetime (minutes)',
      min: 1,
      max: undefined,
      step: '1',
    },
    { key: 'feeBps', label: 'Fee model (bps)', min: 0, max: 100, step: 'any' },
    {
      key: 'volumeRatio',
      label: 'Entry volume ratio',
      min: 1,
      max: 10,
      step: 'any',
    },
  ] as const;

  public override context: ContextType<typeof TradingServiceContext.value> =
    null;

  public override state: TradingPanelState = {
    overview: null,
    error: null,
    validationError: null,
    open: false,
    busy: false,
    dirty: false,
    reviewOpen: false,
    expiresAt: '',
  };

  private mounted = false;

  private timer: number | null = null;

  private request: AbortController | null = null;

  private action: AbortController | null = null;

  private actionInFlight = false;

  private generation = 0;

  public override componentDidMount(): void {
    this.mounted = true;
    void this.load();
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.request?.abort();
    this.action?.abort();
    this.clearTimer();
  }

  public override render(): ReactNode {
    const overview = this.state.overview;
    const heartbeat = overview?.heartbeatAt
      ? Date.parse(overview.heartbeatAt)
      : NaN;
    const liveUnknown =
      !!this.state.error ||
      (overview?.mode === 'autoLive' &&
        (!Number.isFinite(heartbeat) ||
          Date.now() - heartbeat > 120_000 ||
          heartbeat - Date.now() > 30_000));
    const liveOn =
      !liveUnknown &&
      overview?.mode === 'autoLive' &&
      !overview.emergencyStopped &&
      overview.mandate !== null &&
      overview.mandate.revokedAt === null &&
      overview.mandate.accountKey === overview.accountKey &&
      overview.mandate.rulesHash === overview.rulesHash &&
      Date.parse(overview.mandate.expiresAt) > Date.now();
    let label = 'Auto trading off';
    let statusColor: 'default' | 'warning' | 'error' | 'success' = 'default';
    let operatingDetail = `AutoLive ${liveOn ? 'ON' : 'OFF'}.`;
    if (overview?.mode === 'manualReview') {
      label = 'Manual review';
    }

    if (overview?.mode === 'paper') {
      label = 'Paper trading';
      operatingDetail = 'Simulation only. No real orders.';
    }

    if (liveOn) {
      label = 'Auto trading on';
      statusColor = 'success';
    }

    if (liveUnknown) {
      label = 'Status unavailable';
      statusColor = 'warning';
      operatingDetail = 'Live status is unknown.';
    }

    if (overview?.emergencyStopped) {
      label = 'Emergency stopped';
      statusColor = 'error';
    }

    return (
      <Paper
        variant="outlined"
        sx={{ borderRadius: '8px', minWidth: 0, overflow: 'hidden' }}
      >
        <Stack
          spacing={2}
          sx={{ p: { xs: 2, sm: 3 } }}
        >
          <Stack
            direction="row"
            spacing={1}
            sx={{
              alignItems: 'center',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
            }}
          >
            <Stack
              direction="row"
              spacing={1}
              sx={{ alignItems: 'center' }}
            >
              <ShowChartOutlinedIcon fontSize="small" />
              <Typography
                component="h2"
                sx={{ fontSize: 18, fontWeight: 700 }}
              >
                Trading
              </Typography>
            </Stack>
            <Chip
              size="small"
              label={!overview && !this.state.error ? 'Checking status' : label}
              color={statusColor}
            />
          </Stack>
          {this.state.error && (
            <Alert
              severity="error"
              action={
                <Button
                  color="inherit"
                  disabled={this.state.busy}
                  onClick={() => void this.load()}
                >
                  Retry
                </Button>
              }
            >
              {this.state.error}
            </Alert>
          )}
          {overview?.fault && (
            <Alert severity="warning">{overview.fault}</Alert>
          )}
          {!overview && !this.state.error && (
            <Typography variant="body2">Loading trading status…</Typography>
          )}
          {overview && (
            <>
              <Typography
                variant="body2"
                color="text.secondary"
              >
                {operatingDetail}
                {' · '}Last update {TradingPanel.time(overview.heartbeatAt)}
              </Typography>
              {overview.emergencyStopped && (
                <Alert severity="error">
                  Emergency stop is active. New entries are blocked. Existing
                  orders and positions still require reconciliation.
                </Alert>
              )}
              {this.renderBook(overview)}
              <Box
                sx={{
                  display: 'flex',
                  gap: 1,
                  flexWrap: 'wrap',
                  '& .MuiButton-root': { minHeight: 44 },
                }}
              >
                {!liveOn && overview.liveActivationAvailable && (
                  <Button
                    variant="contained"
                    disableElevation
                    disabled={
                      this.state.busy ||
                      this.state.dirty ||
                      !!this.state.error ||
                      overview.emergencyStopped ||
                      overview.mode === 'autoLive'
                    }
                    onClick={() => {
                      this.setState({ reviewOpen: true, expiresAt: '' });
                    }}
                  >
                    Review live mandate
                  </Button>
                )}
                <Tooltip title="Trading settings">
                  <IconButton
                    aria-label="Trading settings"
                    onClick={() => {
                      this.setState({ open: true });
                    }}
                    sx={{
                      width: 44,
                      height: 44,
                      border: '1px solid #e0e0e0',
                      borderRadius: '6px',
                    }}
                  >
                    <SettingsOutlinedIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
                {(overview.mode === 'autoLive' ||
                  overview.mode === 'manualReview') && (
                  <Button
                    variant="outlined"
                    disabled={this.state.busy}
                    onClick={() =>
                      void this.run((signal) =>
                        TradingServiceContext.require(this.context).disable(
                          signal,
                        ),
                      )
                    }
                  >
                    Disable
                  </Button>
                )}
                <Button
                  color="error"
                  variant="outlined"
                  disabled={this.state.busy}
                  onClick={() =>
                    void this.run((signal) =>
                      TradingServiceContext.require(this.context).emergencyStop(
                        signal,
                      ),
                    )
                  }
                >
                  Emergency stop
                </Button>
              </Box>
              {this.state.dirty && (
                <Alert severity="info">
                  Unsaved settings. Save them in Trading settings before
                  reviewing live trading.
                </Alert>
              )}
              {overview.mandate && liveOn && (
                <Typography
                  variant="body2"
                  sx={{ overflowWrap: 'anywhere' }}
                >
                  {overview.mandate.revokedAt
                    ? 'Live permission revoked'
                    : 'Live permission expires'}{' '}
                  {TradingPanel.time(
                    overview.mandate.revokedAt ?? overview.mandate.expiresAt,
                  )}
                </Typography>
              )}
              {!overview.liveActivationAvailable && (
                <Typography
                  variant="body2"
                  color="text.secondary"
                >
                  Live activation is unavailable. The server must have owner
                  controls and live exchange access configured.
                </Typography>
              )}
              <Divider />
              <Box
                component="details"
                sx={{
                  '& > summary': {
                    cursor: 'pointer',
                    minHeight: 44,
                    alignContent: 'center',
                    fontSize: 14,
                    fontWeight: 600,
                  },
                }}
              >
                <Box component="summary">
                  Daily report · {this.props.date || 'Select a date'}
                </Box>
                {this.renderReports(overview)}
              </Box>
              <Drawer
                anchor="right"
                open={this.state.open}
                onClose={() => {
                  this.setState({ open: false });
                }}
                ModalProps={{ keepMounted: true }}
                slotProps={{
                  paper: {
                    role: 'dialog',
                    'aria-modal': true,
                    'aria-labelledby': 'trading-settings-title',
                    sx: {
                      width: { xs: '100%', sm: 600 },
                      maxWidth: '100%',
                      p: { xs: 2, sm: 3 },
                      borderRadius: 0,
                    },
                  },
                }}
              >
                <Stack
                  direction="row"
                  sx={{
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    mb: 1,
                    position: 'sticky',
                    top: 0,
                    bgcolor: 'background.paper',
                    zIndex: 1,
                  }}
                >
                  <Typography
                    id="trading-settings-title"
                    component="h2"
                    sx={{ fontSize: 20, fontWeight: 700 }}
                  >
                    Trading settings
                  </Typography>
                  <IconButton
                    aria-label="Close trading settings"
                    onClick={() => {
                      this.setState({ open: false });
                    }}
                    sx={{ width: 44, height: 44 }}
                  >
                    <CloseOutlinedIcon />
                  </IconButton>
                </Stack>
                <Box
                  role="region"
                  aria-labelledby="trading-settings-title"
                >
                  {this.state.error && (
                    <Alert
                      severity="error"
                      action={
                        <Button
                          color="inherit"
                          disabled={this.state.busy}
                          onClick={() => void this.load()}
                        >
                          Retry
                        </Button>
                      }
                    >
                      {this.state.error}
                    </Alert>
                  )}
                  {this.renderConfiguration(overview)}
                </Box>
                <Divider sx={{ my: 3 }} />
                <Typography
                  component="h3"
                  sx={{ fontSize: 15, fontWeight: 700, mb: 1 }}
                >
                  Operating mode
                </Typography>
                <Typography
                  variant="body2"
                  color="text.secondary"
                  sx={{ mb: 1 }}
                >
                  Switching mode disarms live trading. Paper uses simulated
                  funds; manual review requires approval for each trade.
                </Typography>
                <Stack
                  direction="row"
                  spacing={1}
                >
                  <Button
                    variant="outlined"
                    disabled={
                      this.state.busy ||
                      !!this.state.error ||
                      overview.mode === 'paper'
                    }
                    onClick={() => void this.setMode('paper')}
                  >
                    Paper
                  </Button>
                  <Button
                    variant="outlined"
                    disabled={
                      this.state.busy ||
                      !!this.state.error ||
                      overview.mode === 'manualReview'
                    }
                    onClick={() => void this.setMode('manualReview')}
                  >
                    Manual review
                  </Button>
                </Stack>
              </Drawer>
              <Dialog
                open={this.state.reviewOpen}
                onClose={() => {
                  this.setState({ reviewOpen: false });
                }}
                maxWidth="sm"
                fullWidth
                aria-labelledby="live-review-title"
              >
                <DialogTitle id="live-review-title">
                  Enable auto trading
                </DialogTitle>
                <DialogContent>{this.renderMandate(overview)}</DialogContent>
                <DialogActions>
                  <Button
                    onClick={() => {
                      this.setState({ reviewOpen: false });
                    }}
                    sx={{ minHeight: 44 }}
                  >
                    Cancel
                  </Button>
                </DialogActions>
              </Dialog>
            </>
          )}
        </Stack>
      </Paper>
    );
  }

  private renderBook(overview: TradingOverview): ReactNode {
    const book = overview.mode === 'paper' ? overview.paper : overview.live;
    const openOrders = book.orders.filter(
      (order) =>
        !['filled', 'cancelled', 'rejected', 'expired'].includes(
          order.status.toLowerCase(),
        ),
    );
    const exposure = book.positions.reduce(
      (sum, position) => sum + Number(position.costThb),
      0,
    );

    return (
      <Stack spacing={2}>
        <Box
          component="dl"
          aria-label={`${overview.mode === 'paper' ? 'Paper' : 'Live'} trading balances`}
          sx={{
            m: 0,
            display: 'grid',
            gridTemplateColumns: {
              xs: 'repeat(2, minmax(0, 1fr))',
              sm: 'repeat(4, minmax(0, 1fr))',
            },
            gap: 2,
            py: 2,
            borderBlock: '1px solid #e0e0e0',
          }}
        >
          {[
            ['Cash', TradingPanel.money(book.cashThb)],
            ['In positions', TradingPanel.money(String(exposure))],
            ['Realized net P&L', TradingPanel.money(book.realizedNetThb)],
            ['Open orders', String(openOrders.length)],
          ].map(([name, value]) => (
            <Box key={name}>
              <Typography
                component="dt"
                variant="caption"
                color="text.secondary"
              >
                {name}
              </Typography>
              <Typography
                component="dd"
                sx={{
                  m: 0,
                  mt: 0.5,
                  fontSize: 18,
                  fontWeight: 600,
                  fontVariantNumeric: 'tabular-nums',
                }}
              >
                {value}
              </Typography>
            </Box>
          ))}
        </Box>
        <Typography
          variant="body2"
          color="text.secondary"
        >
          {overview.rules.symbols.join(', ')} ·{' '}
          {TradingPanel.money(String(overview.rules.allocatedThb))} allocated ·{' '}
          {TradingPanel.money(String(overview.rules.perOrderThb))} per order
        </Typography>
        <Box
          component="details"
          sx={{
            '& > summary': {
              cursor: 'pointer',
              minHeight: 44,
              alignContent: 'center',
              fontSize: 14,
              fontWeight: 600,
            },
          }}
        >
          <Box component="summary">
            Positions &amp; orders · {book.positions.length} positions ·{' '}
            {openOrders.length} open
          </Box>
          <Stack
            spacing={1.5}
            sx={{ py: 1 }}
          >
            <Typography variant="body2">
              {overview.mode === 'paper' ? 'Paper' : 'Live'} book · Equity{' '}
              {TradingPanel.money(book.equityThb)} · Gross P&amp;L{' '}
              {TradingPanel.money(book.realizedGrossThb)} · Fees{' '}
              {TradingPanel.money(book.feesThb)} · Unrealized{' '}
              {TradingPanel.money(book.unrealizedThb)}
            </Typography>
            {book.positions.length === 0 && openOrders.length === 0 && (
              <Typography
                variant="body2"
                color="text.secondary"
              >
                No positions or open orders in this book.
              </Typography>
            )}
            {book.positions.map((position) => (
              <Typography
                key={position.symbol}
                variant="body2"
              >
                {position.symbol}: {position.quantity} · Cost{' '}
                {TradingPanel.money(position.costThb)} · Opened{' '}
                {TradingPanel.time(position.openedAt)}
              </Typography>
            ))}
            {openOrders.map((order) => (
              <Typography
                key={order.id}
                variant="body2"
                sx={{ overflowWrap: 'anywhere' }}
              >
                {order.side.toUpperCase()} {order.symbol} · Amount{' '}
                {order.amount} · Limit {order.limitPrice} THB · Remaining{' '}
                {order.remaining} · {order.status} · {order.reason}
              </Typography>
            ))}
            <Typography
              variant="caption"
              color="text.secondary"
            >
              P&amp;L is Unknown until fills and cost are verified. Disable
              stops new trading; existing orders may remain open.
            </Typography>
            {overview.mandate && (
              <Typography
                variant="caption"
                sx={{ overflowWrap: 'anywhere' }}
              >
                Last mandate: {overview.mandate.id} ·{' '}
                {overview.mandate.revokedAt ? 'Revoked' : 'Expires'}{' '}
                {TradingPanel.time(
                  overview.mandate.revokedAt ?? overview.mandate.expiresAt,
                )}
              </Typography>
            )}
          </Stack>
        </Box>
      </Stack>
    );
  }

  private renderConfiguration(overview: TradingOverview): ReactNode {
    return (
      <Box
        key={overview.rulesHash}
        component="form"
        onChange={() => {
          this.setState({
            dirty: true,
            reviewOpen: false,
            validationError: null,
          });
        }}
        onSubmit={(event) => {
          event.preventDefault();
          void this.save(new FormData(event.currentTarget));
        }}
      >
        <Stack
          spacing={2}
          sx={{ pt: 1 }}
        >
          {this.state.validationError && (
            <Alert severity="error">{this.state.validationError}</Alert>
          )}
          <Typography
            variant="body2"
            color="text.secondary"
          >
            Set your budget and limits here. The engine follows the saved rules;
            the AI team researches separately.
          </Typography>
          <Alert severity="warning">
            Saving changes turns auto trading off. Review and enable again to
            use the new rules with real money.
          </Alert>
          <Typography
            component="h3"
            sx={{ fontSize: 15, fontWeight: 700 }}
          >
            Budget &amp; markets
          </Typography>
          <input
            type="hidden"
            name="version"
            value={overview.rules.version}
          />
          <TextField
            label="Allowed symbols"
            name="symbols"
            defaultValue={overview.rules.symbols.join(', ')}
            required
            helperText="For example BTC, ETH. Up to 16 symbols; THB spot only."
            disabled={this.state.busy}
          />
          {this.renderRuleFields(overview, false)}
          <Box
            component="details"
            sx={{
              '& > summary': {
                cursor: 'pointer',
                minHeight: 44,
                alignContent: 'center',
                fontSize: 14,
                fontWeight: 600,
              },
            }}
          >
            <Box component="summary">
              Advanced settings · strategy &amp; execution
            </Box>
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ my: 2 }}
            >
              EMA 20/50 breakout v1 · 5-minute candles. 100 basis points (bps) =
              1%.
            </Typography>
            {this.renderRuleFields(overview, true)}
          </Box>
          <Button
            variant="contained"
            disableElevation
            type="submit"
            disabled={
              this.state.busy || !this.state.dirty || !!this.state.error
            }
            sx={{ alignSelf: 'flex-start', minHeight: 44 }}
          >
            Save rules and disarm live
          </Button>
        </Stack>
      </Box>
    );
  }

  private renderRuleFields(
    overview: TradingOverview,
    advanced: boolean,
  ): ReactNode {
    return (
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: {
            xs: 'minmax(0, 1fr)',
            sm: 'repeat(2, minmax(0, 1fr))',
          },
          gap: 2,
        }}
      >
        {(advanced
          ? TradingPanel.fields.slice(4)
          : TradingPanel.fields.slice(0, 4)
        ).map((field) => (
          <TextField
            key={field.key}
            label={field.label}
            name={field.key}
            type="number"
            defaultValue={overview.rules[field.key]}
            required
            disabled={this.state.busy}
            slotProps={{
              htmlInput: {
                min: field.min,
                max: field.max,
                step: field.step,
              },
            }}
          />
        ))}
      </Box>
    );
  }

  private renderMandate(overview: TradingOverview): ReactNode {
    const expiry = new Date(this.state.expiresAt);
    const validExpiry =
      Number.isFinite(expiry.getTime()) &&
      expiry.getTime() > Date.now() &&
      expiry.getTime() <= Date.now() + 30 * 24 * 60 * 60 * 1000;
    let expiryHelp = 'Choose an expiry to enable auto trading.';
    if (this.state.expiresAt) {
      expiryHelp = 'Expiry must be within the next 30 days.';
      if (
        !Number.isFinite(expiry.getTime()) ||
        expiry.getTime() <= Date.now()
      ) {
        expiryHelp = 'Choose a future date and time.';
      }

      if (validExpiry) {
        expiryHelp = `Expires ${TradingPanel.time(expiry.toISOString())}`;
      }
    }

    return (
      <Stack
        spacing={1.5}
        sx={{ pt: 1 }}
      >
        <Alert severity="warning">
          Real money. The engine will buy and sell within these limits until
          expiry, without asking for each trade. AI reports do not authorize
          orders.
        </Alert>
        <Typography
          variant="body2"
          sx={{ overflowWrap: 'anywhere' }}
        >
          Account: {overview.accountKey}
          <br />
          THB spot symbols: {overview.rules.symbols.join(', ')}
        </Typography>
        <Box
          component="dl"
          sx={{
            m: 0,
            display: 'grid',
            gridTemplateColumns: 'minmax(0, 1fr) auto',
            columnGap: 2,
            rowGap: 0.5,
            fontSize: 14,
          }}
        >
          {TradingPanel.fields.slice(0, 4).map((field) => (
            <Box
              key={field.key}
              sx={{ display: 'contents' }}
            >
              <Box component="dt">{field.label}</Box>
              <Box
                component="dd"
                sx={{
                  m: 0,
                  textAlign: 'right',
                  fontVariantNumeric: 'tabular-nums',
                }}
              >
                {overview.rules[field.key]}
              </Box>
            </Box>
          ))}
        </Box>
        <Box
          component="details"
          sx={{
            '& > summary': {
              cursor: 'pointer',
              minHeight: 44,
              alignContent: 'center',
              fontSize: 14,
              fontWeight: 600,
            },
          }}
        >
          <Box component="summary">Strategy &amp; all limits</Box>
          <Typography
            variant="body2"
            sx={{ overflowWrap: 'anywhere', mb: 1 }}
          >
            Strategy: {overview.rules.version}
            <br />
            Rules hash: {overview.rulesHash}
          </Typography>
          <Box
            component="dl"
            sx={{
              m: 0,
              fontSize: 14,
              display: 'grid',
              gridTemplateColumns: 'minmax(0, 1fr) auto',
              gap: 1,
            }}
          >
            {TradingPanel.fields.slice(4).map((field) => (
              <Box
                key={field.key}
                sx={{ display: 'contents' }}
              >
                <Box component="dt">{field.label.replace(' (bps)', '')}</Box>
                <Box
                  component="dd"
                  sx={{
                    m: 0,
                    textAlign: 'right',
                    fontVariantNumeric: 'tabular-nums',
                  }}
                >
                  {field.key.endsWith('Bps')
                    ? `${String(overview.rules[field.key] / 100)}%`
                    : overview.rules[field.key]}
                </Box>
              </Box>
            ))}
          </Box>
        </Box>
        <Typography
          component="h3"
          sx={{ fontSize: 14, fontWeight: 600 }}
        >
          When should permission expire?
        </Typography>
        <Stack
          direction="row"
          spacing={1}
        >
          {[1, 7, 30].map((days) => (
            <Button
              key={days}
              variant="outlined"
              disabled={this.state.busy}
              onClick={() => {
                this.setState({
                  expiresAt: TradingPanel.localDateTime(
                    Date.now() + days * 24 * 60 * 60 * 1000,
                  ),
                });
              }}
              sx={{ minHeight: 44 }}
            >
              {days} {days === 1 ? 'day' : 'days'}
            </Button>
          ))}
        </Stack>
        <TextField
          type="datetime-local"
          label="Mandate expiry (your local time)"
          required
          error={!!this.state.expiresAt && !validExpiry}
          value={this.state.expiresAt}
          onChange={(event) => {
            this.setState({ expiresAt: event.target.value });
          }}
          slotProps={{
            inputLabel: { shrink: true },
            htmlInput: {
              min: TradingPanel.localDateTime(Date.now()),
              max: TradingPanel.localDateTime(
                Date.now() + 30 * 24 * 60 * 60 * 1000,
              ),
            },
          }}
          helperText={expiryHelp}
          disabled={this.state.busy}
        />
        <Button
          variant="contained"
          disableElevation
          disabled={
            this.state.busy ||
            this.state.dirty ||
            !!this.state.error ||
            !validExpiry ||
            !overview.liveActivationAvailable ||
            overview.emergencyStopped ||
            overview.mode === 'autoLive'
          }
          onClick={() => void this.activate(overview)}
          sx={{ alignSelf: 'flex-start', minHeight: 44 }}
        >
          Enable Auto Trading
        </Button>
      </Stack>
    );
  }

  private renderReports(overview: TradingOverview): ReactNode {
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
              {report.status}
            </Typography>
            <Typography variant="body2">
              Starting equity {TradingPanel.money(report.startingEquityThb)} ·
              Ending equity {TradingPanel.money(report.endingEquityThb)} · Net
              P&amp;L {TradingPanel.money(report.realizedNetThb)}
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

  private static money(value: string | null): string {
    if (value === null) {
      return 'Unknown';
    }

    return `฿${Number(value).toLocaleString('en-GB', { maximumFractionDigits: 2 })}`;
  }

  private static localDateTime(timestamp: number): string {
    const date = new Date(timestamp);

    return new Date(timestamp - date.getTimezoneOffset() * 60_000)
      .toISOString()
      .slice(0, 16);
  }

  private static time(value: string | null): string {
    if (value === null) {
      return 'Unavailable';
    }

    return `${new Date(value).toLocaleString('en-GB', { timeZone: 'Asia/Bangkok' })} Bangkok`;
  }

  private async save(data: FormData): Promise<void> {
    const overview = this.state.overview;
    if (!overview) {
      return;
    }

    const numbers = Object.fromEntries(
      TradingPanel.fields.map((field) => [
        field.key,
        Number(data.get(field.key)),
      ]),
    );
    const symbols = data.get('symbols');
    const rules = tradingRulesSchema.safeParse({
      ...overview.rules,
      ...numbers,
      version: data.get('version'),
      symbols: (typeof symbols === 'string' ? symbols : '')
        .split(',')
        .map((symbol) => symbol.trim()),
    });
    if (!rules.success) {
      this.setState({
        validationError:
          rules.error.issues.at(0)?.message ?? 'Invalid trading rules.',
      });

      return;
    }

    await this.run(
      (signal) =>
        TradingServiceContext.require(this.context).configure(
          rules.data,
          signal,
        ),
      true,
    );
  }

  private async setMode(mode: 'paper' | 'manualReview'): Promise<void> {
    await this.run((signal) =>
      TradingServiceContext.require(this.context).setMode(mode, signal),
    );
  }

  private async activate(overview: TradingOverview): Promise<void> {
    const expiry = new Date(this.state.expiresAt);
    if (
      !this.state.reviewOpen ||
      this.state.dirty ||
      this.state.error ||
      !overview.liveActivationAvailable ||
      overview.emergencyStopped ||
      overview.mode === 'autoLive' ||
      !Number.isFinite(expiry.getTime()) ||
      expiry.getTime() <= Date.now() ||
      expiry.getTime() > Date.now() + 30 * 24 * 60 * 60 * 1000 ||
      this.state.overview?.rulesHash !== overview.rulesHash ||
      this.state.overview.accountKey !== overview.accountKey
    ) {
      return;
    }

    await this.run((signal) =>
      TradingServiceContext.require(this.context).activate(
        overview.accountKey,
        overview.rulesHash,
        expiry.toISOString(),
        signal,
      ),
    );
  }

  private async load(): Promise<void> {
    if (this.actionInFlight) {
      return;
    }

    this.clearTimer();
    this.request?.abort();
    const request = new AbortController();
    this.request = request;
    const generation = ++this.generation;
    try {
      const overview = await TradingServiceContext.require(this.context).get(
        request.signal,
      );
      if (this.mounted && this.generation === generation) {
        this.receive(overview);
      }
    } catch (error) {
      if (
        this.mounted &&
        !request.signal.aborted &&
        this.generation === generation
      ) {
        this.setState({
          error:
            error instanceof Error
              ? error.message
              : 'Unable to load trading status.',
          reviewOpen: false,
        });
      }
    } finally {
      if (this.mounted && this.generation === generation) {
        this.schedule();
      }
    }
  }

  private receive(overview: TradingOverview, saved = false): void {
    this.setState((state) => {
      const changed =
        state.overview?.rulesHash !== overview.rulesHash ||
        state.overview.accountKey !== overview.accountKey;

      return {
        overview,
        error: null,
        dirty: saved || changed ? false : state.dirty,
        reviewOpen: changed || saved ? false : state.reviewOpen,
      };
    });
  }

  private async run(
    call: (signal: AbortSignal) => Promise<TradingOverview>,
    saved = false,
  ): Promise<void> {
    if (this.actionInFlight) {
      return;
    }

    this.actionInFlight = true;
    this.generation += 1;
    this.request?.abort();
    this.clearTimer();
    this.action = new AbortController();
    this.setState({ busy: true, error: null, reviewOpen: false });
    try {
      const overview = await call(this.action.signal);
      if (this.mounted) {
        this.receive(overview, saved);
      }
    } catch (error) {
      if (this.mounted && !this.action.signal.aborted) {
        this.setState({
          error:
            error instanceof Error
              ? error.message
              : 'Trading control failed. Refresh status before trying again.',
        });
      }
    } finally {
      this.actionInFlight = false;
      if (this.mounted) {
        this.setState({ busy: false });
        this.schedule();
      }
    }
  }

  private schedule(): void {
    this.clearTimer();
    this.timer = window.setTimeout(() => void this.load(), 10000);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      window.clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
