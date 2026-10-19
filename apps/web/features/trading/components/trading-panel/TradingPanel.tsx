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
                onStageAdaptiveUpgrade={() => {
                  this.stageAdaptiveUpgrade();
                }}
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
        .map((field) => [field.key, Number(data.get(field.key))]),
    );
    const symbols = data.get('symbols');
    const candidate = {
      ...overview.rules,
      ...numbers,
      symbols: overview.rules.adaptive
        ? []
        : (typeof symbols === 'string' ? symbols : '')
            .split(',')
            .map((symbol) => symbol.trim()),
    };
    const adaptive = candidate.adaptive;
    const rules = tradingRulesSchema.safeParse(
      adaptive
        ? {
            ...candidate,
            adaptive: {
              ...adaptive,
              dailyLossFraction: Number(data.get('dailyLossFraction')) / 100,
              risk: {
                ...adaptive.risk,
                maximumExposureFraction:
                  Number(data.get('maximumExposureFraction')) / 100,
                maximumConcentrationFraction:
                  Number(data.get('maximumConcentrationFraction')) / 100,
                riskToStopFraction:
                  Number(data.get('riskToStopFraction')) / 100,
                estimatedFeeRate: candidate.feeBps / 10000,
                maximumSpreadFraction: candidate.maxSpreadBps / 10000,
                maximumDataAgeSeconds: candidate.maxDataAgeSeconds,
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
    const rules = tradingRulesSchema.safeParse({
      ...overview.rules,
      version: 'adaptive-intraday-v2',
      symbols: [],
      maxPositions: 16,
      adaptive: {
        universeVersion: 'bitkub-thb-spot-v1',
        includeFutureDeposits: true,
        excludedSymbols: ['USDT', 'USDC', 'DAI'],
        risk: {
          profile: 'aggressive',
          maximumExposureFraction: 1,
          maximumConcentrationFraction: 1,
          riskToStopFraction: 0.05,
          atrMultiplier: 1,
          maximumSpreadFraction: overview.rules.maxSpreadBps / 10000,
          estimatedFeeRate: overview.rules.feeBps / 10000,
          maximumDataAgeSeconds: overview.rules.maxDataAgeSeconds,
          maximumSignalAgeSeconds: 600,
        },
        dailyLossFraction: 0.1,
        dailyProfitTargetThb: 500,
      },
    });

    if (!rules.success) {
      this.setState({
        validationError:
          rules.error.issues.at(0)?.message ??
          'Unable to prepare adaptive trading.',
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
    if (this.mounted) {
      this.setState((state) =>
        state.overview &&
        TradingRulePresentation.isAdaptive(state.overview.rules.version) &&
        !state.error
          ? { reviewOpen: true, expiresAt: '' }
          : null,
      );
    }
  }

  private stageAdaptiveUpgrade(): void {
    this.setState((state) =>
      state.overview
        ? {
            overview: {
              ...state.overview,
              rules: {
                ...state.overview.rules,
                version: 'adaptive-intraday-v2',
              },
            },
            dirty: true,
          }
        : null,
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
  ): Promise<void> {
    if (this.actionInFlight) {
      return;
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
