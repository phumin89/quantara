'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  Divider,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import ShowChartOutlinedIcon from '@mui/icons-material/ShowChartOutlined';
import { TradingServiceContext } from '../../context/TradingServiceContext';
import type { TradingOverview } from '../../contracts/TradingOverview';
import { tradingRulesSchema } from '../../contracts/TradingRules';
import type { TradingPanelProps } from './TradingPanelProps';
import type { TradingPanelState } from './TradingPanelState';
import TradingFormatting from '../../presentation/TradingFormatting';
import TradingRulePresentation from '../../presentation/TradingRulePresentation';
import TradingBookView from '../trading-book-view/TradingBookView';
import TradingAutonomyView from '../trading-autonomy-view/TradingAutonomyView';
import TradingRealtimeView from '../trading-realtime-view/TradingRealtimeView';
import TradingReportsView from '../trading-reports-view/TradingReportsView';
import TradingSettings from '../trading-settings/TradingSettings';
import TradingMandateDialog from '../trading-mandate-dialog/TradingMandateDialog';
import TradingControls from '../trading-controls/TradingControls';

export default class TradingPanel extends Component<
  TradingPanelProps,
  TradingPanelState
> {
  public static override readonly contextType = TradingServiceContext.value;

  public override context: ContextType<typeof TradingServiceContext.value> =
    null;

  public override state: TradingPanelState = {
    overview: null,
    error: null,
    actionError: null,
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
    const displayError = this.state.error ?? this.state.actionError;
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
              label={!overview && !displayError ? 'Checking status' : label}
              color={statusColor}
            />
          </Stack>
          {displayError && (
            <Alert
              severity="error"
              action={
                <Button
                  color="inherit"
                  disabled={this.state.busy}
                  onClick={() => void this.load()}
                >
                  {this.state.error ? 'Retry' : 'Refresh status'}
                </Button>
              }
            >
              {displayError}
            </Alert>
          )}
          {overview?.fault && (
            <Alert severity="warning">{overview.fault}</Alert>
          )}
          {!overview && !displayError && (
            <Typography variant="body2">Loading trading status…</Typography>
          )}
          {overview && (
            <>
              {TradingRulePresentation.isAdaptive(overview.rules.version) && (
                <TradingAutonomyView overview={overview} />
              )}
              <Typography
                variant="body2"
                color="text.secondary"
              >
                {operatingDetail}
                {' · '}Last update{' '}
                {TradingFormatting.time(overview.heartbeatAt)}
              </Typography>
              {overview.realtime && <TradingRealtimeView overview={overview} />}
              {overview.emergencyStopped && (
                <Alert severity="error">
                  Emergency stop is active. New entries are blocked. Existing
                  orders and positions still require reconciliation.
                </Alert>
              )}
              <TradingControls
                overview={overview}
                liveOn={liveOn}
                busy={this.state.busy}
                dirty={this.state.dirty}
                error={this.state.error}
                onReview={() => {
                  if (
                    TradingRulePresentation.isAdaptive(overview.rules.version)
                  ) {
                    this.setState({ reviewOpen: true, expiresAt: '' });
                  } else {
                    void this.prepareAdaptiveSetup(overview);
                  }
                }}
                onSettings={() => {
                  this.setState({ open: true });
                }}
                onDisable={() =>
                  void this.run((signal) =>
                    TradingServiceContext.require(this.context).disable(signal),
                  )
                }
                onEmergencyStop={() =>
                  void this.run((signal) =>
                    TradingServiceContext.require(this.context).emergencyStop(
                      signal,
                    ),
                  )
                }
              />
              <TradingBookView overview={overview} />
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
                  {TradingFormatting.time(
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
                <TradingReportsView
                  overview={overview}
                  date={this.props.date}
                />
              </Box>
              <TradingSettings
                overview={overview}
                open={this.state.open}
                busy={this.state.busy}
                dirty={this.state.dirty}
                error={this.state.error}
                validationError={this.state.validationError}
                onClose={() => {
                  this.setState({ open: false });
                }}
                onDirty={() => {
                  this.setState({
                    dirty: true,
                    reviewOpen: false,
                    validationError: null,
                  });
                }}
                onSave={(data) => void this.save(data)}
                onPrepareAdaptiveSetup={() =>
                  void this.prepareAdaptiveSetup(overview)
                }
                onSetMode={(mode) => void this.setMode(mode)}
              />
              <TradingMandateDialog
                overview={overview}
                open={this.state.reviewOpen}
                expiresAt={this.state.expiresAt}
                busy={this.state.busy}
                dirty={this.state.dirty}
                error={this.state.error}
                onClose={() => {
                  this.setState({ reviewOpen: false });
                }}
                onExpiresAtChange={(expiresAt) => {
                  this.setState({ expiresAt });
                }}
                onActivate={() => void this.activate(overview)}
              />
            </>
          )}
        </Stack>
      </Paper>
    );
  }

  private async save(data: FormData): Promise<void> {
    const overview = this.state.overview;
    if (!overview) {
      return;
    }

    const numbers = Object.fromEntries(
      TradingRulePresentation.fields
        .filter((field) => data.has(field.key))
        .map((field) => {
          const value = data.get(field.key);

          return [
            field.key,
            typeof value === 'string' && value.trim() !== ''
              ? Number(value)
              : NaN,
          ];
        }),
    );
    const symbols = data.get('symbols');
    const candidate = {
      ...overview.rules,
      ...numbers,
      symbols:
        typeof symbols === 'string'
          ? symbols.split(',').map((symbol) => symbol.trim())
          : overview.rules.symbols,
    };
    const adaptive = candidate.adaptive;
    const rules = tradingRulesSchema.safeParse(
      adaptive
        ? {
            ...candidate,
            adaptive: {
              ...adaptive,
              dailyLossFraction: this.percentOverride(
                data,
                'dailyLossFraction',
                adaptive.dailyLossFraction,
              ),
              risk: {
                ...adaptive.risk,
                maximumExposureFraction: this.percentOverride(
                  data,
                  'maximumExposureFraction',
                  adaptive.risk.maximumExposureFraction,
                ),
                maximumConcentrationFraction: this.percentOverride(
                  data,
                  'maximumConcentrationFraction',
                  adaptive.risk.maximumConcentrationFraction,
                ),
                riskToStopFraction: this.percentOverride(
                  data,
                  'riskToStopFraction',
                  adaptive.risk.riskToStopFraction,
                ),
                estimatedFeeRate: data.has('feeBps')
                  ? candidate.feeBps / 10000
                  : adaptive.risk.estimatedFeeRate,
                maximumSpreadFraction: data.has('maxSpreadBps')
                  ? candidate.maxSpreadBps / 10000
                  : adaptive.risk.maximumSpreadFraction,
                maximumDataAgeSeconds: data.has('maxDataAgeSeconds')
                  ? candidate.maxDataAgeSeconds
                  : adaptive.risk.maximumDataAgeSeconds,
              },
            },
          }
        : candidate,
    );
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

  private async prepareAdaptiveSetup(overview: TradingOverview): Promise<void> {
    if (
      this.state.dirty ||
      this.state.error ||
      this.state.overview?.accountKey !== overview.accountKey ||
      this.state.overview.rulesHash !== overview.rulesHash ||
      overview.rules.version === 'adaptive-intraday-v3'
    ) {
      return;
    }

    const prepared = await this.run(
      (signal) =>
        TradingServiceContext.require(this.context).prepare(
          overview.accountKey,
          overview.rulesHash,
          signal,
        ),
      true,
    );
    if (this.mounted && prepared) {
      this.setState((state) =>
        state.overview?.accountKey === prepared.accountKey &&
        state.overview.rulesHash === prepared.rulesHash &&
        TradingRulePresentation.isAdaptive(prepared.rules.version) &&
        !state.error
          ? { open: false, reviewOpen: true, expiresAt: '' }
          : null,
      );
    }
  }

  private percentOverride(data: FormData, name: string, saved: number): number {
    const value = data.get(name);
    if (value === null || value === String(saved * 100)) {
      return saved;
    }

    return typeof value === 'string' && value.trim() !== ''
      ? Number(value) / 100
      : NaN;
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
        overview:
          !saved && !changed && state.dirty
            ? { ...overview, rules: state.overview.rules }
            : overview,
        error: null,
        dirty: saved || changed ? false : state.dirty,
        reviewOpen: changed || saved ? false : state.reviewOpen,
      };
    });
  }

  private async run(
    call: (signal: AbortSignal) => Promise<TradingOverview>,
    saved = false,
  ): Promise<TradingOverview | null> {
    if (this.actionInFlight) {
      return null;
    }

    this.actionInFlight = true;
    this.generation += 1;
    this.request?.abort();
    this.clearTimer();
    this.action = new AbortController();
    this.setState({
      busy: true,
      error: null,
      actionError: null,
      reviewOpen: false,
    });
    try {
      const overview = await call(this.action.signal);
      if (this.mounted) {
        this.receive(overview, saved);
        this.setState({ actionError: null });

        return overview;
      }
    } catch (error) {
      if (this.mounted && !this.action.signal.aborted) {
        this.setState({
          error:
            error instanceof Error
              ? error.message
              : 'Trading control failed. Refresh status before trying again.',
          actionError:
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

    return null;
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
