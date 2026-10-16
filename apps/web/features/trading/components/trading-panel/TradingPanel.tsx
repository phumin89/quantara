'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  Collapse,
  Divider,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
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
    const label = this.state.error
      ? 'Status unavailable'
      : (overview?.mode ?? 'Loading');

    return (
      <Paper
        variant="outlined"
        sx={{ p: { xs: 1.5, sm: 2 }, borderRadius: '8px', minWidth: 0 }}
      >
        <Stack spacing={2}>
          <Stack
            direction="row"
            spacing={1}
            sx={{
              alignItems: 'center',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
            }}
          >
            <Typography
              component="h2"
              sx={{ fontSize: 16, fontWeight: 700 }}
            >
              Trading
            </Typography>
            <Chip
              size="small"
              label={label}
              color={overview?.emergencyStopped ? 'error' : 'default'}
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
                sx={{ overflowWrap: 'anywhere' }}
              >
                Account: {overview.accountKey}
              </Typography>
              <Typography
                variant="body2"
                color="text.secondary"
              >
                {liveUnknown
                  ? 'Live status is unknown.'
                  : `AutoLive ${liveOn ? 'ON' : 'OFF'}.`}{' '}
                Heartbeat: {TradingPanel.time(overview.heartbeatAt)}
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
              <Typography
                variant="caption"
                color="text.secondary"
              >
                Paper uses simulated funds. Manual review keeps individual trade
                approval. AutoLive runs deterministic rules while its mandate is
                valid.
              </Typography>
              {overview.mandate && (
                <Typography
                  variant="body2"
                  sx={{ overflowWrap: 'anywhere' }}
                >
                  Mandate {overview.mandate.id} ·{' '}
                  {overview.mandate.revokedAt ? 'Revoked' : 'Expires'}{' '}
                  {TradingPanel.time(
                    overview.mandate.revokedAt ?? overview.mandate.expiresAt,
                  )}
                </Typography>
              )}
              <Button
                aria-expanded={this.state.open}
                onClick={() => {
                  this.setState({ open: !this.state.open });
                }}
                sx={{ alignSelf: 'flex-start', minHeight: 44 }}
              >
                {this.state.open
                  ? 'Hide configuration'
                  : 'Configure rules and limits'}
              </Button>
              <Collapse in={this.state.open}>
                {this.renderConfiguration(overview)}
              </Collapse>
              {!overview.liveActivationAvailable && (
                <Typography
                  variant="body2"
                  color="text.secondary"
                >
                  Live activation is unavailable. The server must have owner
                  controls and live exchange access configured.
                </Typography>
              )}
              {overview.liveActivationAvailable && !liveOn && (
                <Button
                  variant="outlined"
                  disabled={
                    this.state.busy ||
                    this.state.dirty ||
                    !!this.state.error ||
                    overview.emergencyStopped
                  }
                  onClick={() => {
                    this.setState({
                      reviewOpen: !this.state.reviewOpen,
                      expiresAt: '',
                    });
                  }}
                  aria-expanded={this.state.reviewOpen}
                  sx={{ alignSelf: 'flex-start', minHeight: 44 }}
                >
                  {this.state.reviewOpen
                    ? 'Close mandate review'
                    : 'Review live mandate'}
                </Button>
              )}
              <Collapse in={this.state.reviewOpen}>
                {this.renderMandate(overview)}
              </Collapse>
              <Divider />
              {this.renderReports(overview)}
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
      <Stack spacing={1}>
        <Typography variant="body2">
          {overview.mode === 'paper' ? 'Paper' : 'Live'} book · Cash{' '}
          {TradingPanel.money(book.cashThb)} · Equity{' '}
          {TradingPanel.money(book.equityThb)}
        </Typography>
        <Typography variant="body2">
          Position cost exposure {TradingPanel.money(String(exposure))} ·{' '}
          {book.positions.length} positions · {openOrders.length} open orders
        </Typography>
        <Typography variant="body2">
          Realized gross {TradingPanel.money(book.realizedGrossThb)} · Net{' '}
          {TradingPanel.money(book.realizedNetThb)} · Fees{' '}
          {TradingPanel.money(book.feesThb)} · Unrealized{' '}
          {TradingPanel.money(book.unrealizedThb)}
        </Typography>
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
            {order.side.toUpperCase()} {order.symbol} · Amount {order.amount} ·
            Limit {order.limitPrice} THB · Remaining {order.remaining} ·{' '}
            {order.status} · {order.reason}
          </Typography>
        ))}
        <Typography
          variant="caption"
          color="text.secondary"
        >
          Unknown P&amp;L remains Unknown until fills and cost basis are
          verified. Disable stops new trading; it does not prove existing orders
          were cancelled.
        </Typography>
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
          <Typography variant="body2">
            Save rules before reviewing the mandate. Material changes disarm
            AutoLive. 100 basis points (bps) = 1%.
          </Typography>
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: {
                xs: 'minmax(0, 1fr)',
                md: 'repeat(2, minmax(0, 1fr))',
              },
              gap: 2,
            }}
          >
            <TextField
              select
              label="Strategy version"
              name="version"
              defaultValue={overview.rules.version}
              slotProps={{ select: { native: true } }}
              disabled={this.state.busy}
            >
              <option value="ema20-50-breakout-v1">
                EMA 20/50 breakout v1
              </option>
            </TextField>
            <TextField
              label="Allowed symbols"
              name="symbols"
              defaultValue={overview.rules.symbols.join(', ')}
              required
              helperText="Uppercase base symbols, comma separated; THB spot only."
              disabled={this.state.busy}
            />
            {TradingPanel.fields.map((field) => (
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
          <Button
            variant="outlined"
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

  private renderMandate(overview: TradingOverview): ReactNode {
    const expiry = new Date(this.state.expiresAt);
    const validExpiry =
      Number.isFinite(expiry.getTime()) &&
      expiry.getTime() > Date.now() &&
      expiry.getTime() <= Date.now() + 30 * 24 * 60 * 60 * 1000;

    return (
      <Stack
        spacing={1.5}
        sx={{ pt: 1 }}
      >
        <Alert severity="warning">
          Enabling once authorizes deterministic real-money spot orders within
          these exact rules until expiry, without per-trade approval. AI reports
          do not authorize orders.
        </Alert>
        <Typography
          variant="body2"
          sx={{ overflowWrap: 'anywhere' }}
        >
          Account: {overview.accountKey}
          <br />
          Strategy: {overview.rules.version}
          <br />
          Rules hash: {overview.rulesHash}
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
          {TradingPanel.fields.map((field) => (
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
        <TextField
          type="datetime-local"
          label="Mandate expiry (your local time)"
          value={this.state.expiresAt}
          onChange={(event) => {
            this.setState({ expiresAt: event.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
          helperText={
            validExpiry
              ? `Expires ${TradingPanel.time(expiry.toISOString())}`
              : 'Choose an expiry within the next 30 days.'
          }
          disabled={this.state.busy}
        />
        <Button
          variant="contained"
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
