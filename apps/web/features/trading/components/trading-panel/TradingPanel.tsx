'use client';

import { Component, createRef, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Chip,
  Drawer,
  IconButton,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import ShowChartOutlinedIcon from '@mui/icons-material/ShowChartOutlined';
import CloseOutlinedIcon from '@mui/icons-material/CloseOutlined';
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
import ReconciliationPanel from '../reconciliation-panel/ReconciliationPanel';
import { ReconciliationSafety } from '../../presentation/ReconciliationSafety';

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
    refreshing: false,
    dirty: false,
    reviewOpen: false,
    balanceReviewOpen: false,
    expiresAt: '',
    aiManaged: false,
  };

  private mounted = false;

  private timer: number | null = null;

  private request: AbortController | null = null;

  private action: AbortController | null = null;

  private actionInFlight = false;

  private generation = 0;

  private readonly ordersSection = createRef<HTMLDetailsElement>();

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

  public override componentDidUpdate(): void {
    if (!this.state.reviewOpen && this.state.aiManaged) {
      this.setState({ aiManaged: false });
    }
  }

  public override render(): ReactNode {
    const overview = this.state.overview;
    const balanceIssue = this.balanceIssue;
    const historicalSymbols =
      this.props.portfolio?.performance?.unreconciledSymbols ?? [];
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
    const retainedLiveSymbols = (overview?.live.positions ?? [])
      .filter((position) => Number(position.quantity) > 0)
      .map((position) => position.symbol);
    const automaticExitsOff =
      retainedLiveSymbols.length > 0 &&
      (overview?.mode !== 'autoLive' || overview.emergencyStopped);
    const uncertainOrders = (overview?.live.orders ?? []).filter(
      (order) =>
        ['submitting', 'unknown'].includes(order.status) ||
        ['claimed', 'unknown'].includes(order.cancellationState ?? ''),
    );
    const orderReviewNeeded = uncertainOrders.length > 0;
    const liveBlocked = liveOn && (!!overview.fault || orderReviewNeeded);
    let label = 'Auto trading off';
    let statusColor: 'default' | 'warning' | 'error' | 'success' = 'default';
    let operatingDetail: string | null = null;
    if (overview?.mode === 'manualReview') {
      label = 'Manual review';
      operatingDetail = 'Each order needs your approval.';
    }

    if (overview?.mode === 'paper') {
      label = 'Paper trading';
      operatingDetail = 'Simulation only. No real orders.';
    }

    if (liveOn) {
      label = liveBlocked ? 'Enabled · Blocked' : 'Auto trading on';
      statusColor = liveBlocked ? 'warning' : 'success';
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
        sx={{
          borderRadius: 'var(--radius-panel)',
          borderColor: 'var(--border-panel)',
          bgcolor: 'var(--surface-panel)',
          boxShadow: 'var(--shadow-panel)',
          minWidth: 0,
          overflow: 'hidden',
        }}
      >
        <Stack
          spacing={0}
          sx={{
            p: 'var(--panel-padding)',
            '& > details': {
              bgcolor: 'var(--surface-soft)',
              borderRadius: 'var(--radius-control)',
              px: 1.5,
              mt: 1.5,
            },
          }}
        >
          <Stack
            direction={{ xs: 'column', sm: 'row' }}
            spacing={1}
            sx={{
              alignItems: { xs: 'flex-start', sm: 'center' },
              justifyContent: 'space-between',
              mb: 2,
            }}
          >
            <Stack
              direction="row"
              spacing={1}
              sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1 }}
            >
              <ShowChartOutlinedIcon
                sx={{
                  width: 40,
                  height: 40,
                  p: 0.75,
                  bgcolor: 'var(--color-peach)',
                  color: 'text.primary',
                  borderRadius: 'var(--radius-control)',
                }}
              />
              <Typography
                component="h2"
                sx={{
                  fontFamily: 'var(--font-display)',
                  fontSize: 22,
                  fontWeight: 850,
                }}
              >
                Trading
              </Typography>
              <Chip
                size="small"
                label={!overview && !displayError ? 'Checking status' : label}
                color={statusColor}
              />
            </Stack>
            {overview && (
              <Typography
                variant="caption"
                color="text.secondary"
              >
                Updated {TradingFormatting.time(overview.heartbeatAt)}
              </Typography>
            )}
          </Stack>
          {(this.state.actionError || balanceIssue) && (
            <Alert
              severity="warning"
              sx={{ mb: 2 }}
              action={
                this.state.actionError && !this.state.error ? (
                  <Button
                    color="inherit"
                    disabled={this.state.busy || this.state.refreshing}
                    loading={this.state.refreshing}
                    onClick={() => void this.load()}
                  >
                    Refresh status
                  </Button>
                ) : undefined
              }
            >
              <AlertTitle sx={{ fontSize: 14, fontWeight: 600 }}>
                {balanceIssue ? 'Balance review needed' : 'Check latest status'}
              </AlertTitle>
              {balanceIssue ?? this.state.actionError}
              {balanceIssue && overview?.emergencyStopped && (
                <Typography
                  variant="body2"
                  sx={{ mt: 1 }}
                >
                  Emergency stop is also active. Clear it when ready to return
                  to setup. Trading stays off until you review and enable.
                </Typography>
              )}
              {balanceIssue && this.state.actionError && (
                <Box
                  component="details"
                  sx={{
                    mt: 1,
                    '& > summary': {
                      minHeight: 44,
                      alignContent: 'center',
                      cursor: 'pointer',
                    },
                  }}
                >
                  <Box component="summary">Last attempt</Box>
                  {this.state.actionError}
                </Box>
              )}
            </Alert>
          )}
          {automaticExitsOff && (
            <Alert
              severity="warning"
              sx={{ mb: 2 }}
            >
              <AlertTitle sx={{ fontSize: 14, fontWeight: 600 }}>
                Automatic exits are off
              </AlertTitle>
              The app will not send stop-loss, trailing, or timed-exit orders
              for {retainedLiveSymbols.join(', ')}. Check your holdings and open
              orders in Bitkub.
            </Alert>
          )}
          {!balanceIssue && historicalSymbols.length > 0 && (
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ mb: 2 }}
            >
              Historical cost and profit remain unverified:{' '}
              {historicalSymbols.join(', ')}.
            </Typography>
          )}
          {this.state.error && !this.state.busy && !this.state.open && (
            <Alert
              severity="error"
              sx={{ mb: 2 }}
              action={
                <Button
                  color="inherit"
                  disabled={this.state.refreshing}
                  loading={this.state.refreshing}
                  onClick={() => void this.load()}
                >
                  Refresh status
                </Button>
              }
            >
              {this.state.error}
            </Alert>
          )}
          {orderReviewNeeded && (
            <Alert
              severity="warning"
              sx={{ mb: 2 }}
            >
              <AlertTitle sx={{ fontSize: 14, fontWeight: 600 }}>
                Order confirmation needed
              </AlertTitle>
              Unconfirmed order:{' '}
              {Array.from(
                new Set(uncertainOrders.map((order) => order.symbol)),
              ).join(', ')}
              . Check its details below against Bitkub order history. Auto
              trading stays blocked until the outcome is verified.
              {overview?.fault &&
                overview.fault !==
                  'Uncertain broker order — reconciliation required' && (
                  <Typography
                    variant="body2"
                    sx={{ mt: 1 }}
                  >
                    {overview.fault}
                  </Typography>
                )}
            </Alert>
          )}
          {overview?.fault && !orderReviewNeeded && (
            <Alert
              severity="warning"
              sx={{ mb: 2 }}
            >
              {overview.fault}
            </Alert>
          )}
          {!overview && !displayError && (
            <Typography variant="body2">Loading trading status…</Typography>
          )}
          {overview && (
            <>
              {operatingDetail && (
                <Typography
                  variant="body2"
                  color="text.secondary"
                  sx={{ mb: 2 }}
                >
                  {operatingDetail}
                </Typography>
              )}
              {overview.emergencyStopped && !balanceIssue && (
                <Alert
                  severity="warning"
                  sx={{ mb: 2 }}
                >
                  Emergency stop is active. Clear it when ready to return to
                  setup. Trading stays off until you review and enable. Existing
                  broker orders still need review.
                </Alert>
              )}
              <TradingControls
                overview={overview}
                liveOn={liveOn}
                busy={this.state.busy}
                dirty={this.state.dirty}
                error={this.state.error}
                orderReviewNeeded={orderReviewNeeded}
                balanceReviewNeeded={
                  balanceIssue !== null && this.props.history !== undefined
                }
                onReview={() => {
                  if (orderReviewNeeded) {
                    const section = this.ordersSection.current;
                    if (section) {
                      section.open = true;
                      section.querySelector('summary')?.focus();
                      section.scrollIntoView({ block: 'start' });
                    }
                  } else if (balanceIssue && this.props.history) {
                    this.setState({
                      balanceReviewOpen: true,
                      reviewOpen: false,
                    });
                  } else if (
                    TradingRulePresentation.isAdaptive(overview.rules.version)
                  ) {
                    this.setState(
                      { reviewOpen: false, aiManaged: false },
                      () => {
                        this.setState({ reviewOpen: true, expiresAt: '' });
                      },
                    );
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
              <Box
                component="details"
                ref={this.ordersSection}
                open={orderReviewNeeded}
                sx={{
                  scrollMarginTop: '80px',
                  '& > summary': {
                    minHeight: 44,
                    alignContent: 'center',
                    cursor: 'pointer',
                    fontWeight: 600,
                    fontSize: 14,
                    '&:focus-visible': {
                      outline: '2px solid',
                      outlineOffset: 2,
                    },
                  },
                  borderTop: '1px solid',
                  borderColor: 'divider',
                  mt: 2,
                }}
              >
                <Box component="summary">Orders &amp; positions</Box>
                <TradingBookView overview={overview} />
              </Box>
              <Box
                component="details"
                sx={{
                  borderTop: '1px solid',
                  borderColor: 'divider',
                  '& > summary': {
                    minHeight: 44,
                    alignContent: 'center',
                    cursor: 'pointer',
                    fontWeight: 600,
                    fontSize: 14,
                    '&:focus-visible': {
                      outline: '2px solid',
                      outlineOffset: 2,
                    },
                  },
                }}
              >
                <Box component="summary">System details</Box>
                {TradingRulePresentation.isAdaptive(overview.rules.version) && (
                  <TradingAutonomyView overview={overview} />
                )}
                {overview.realtime && (
                  <TradingRealtimeView overview={overview} />
                )}
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
                  {TradingFormatting.time(
                    overview.mandate.revokedAt ?? overview.mandate.expiresAt,
                  )}
                </Typography>
              )}
              {overview.aiPolicy?.enabled && (
                <Alert
                  severity="info"
                  sx={{ mt: 1 }}
                >
                  <AlertTitle>AI-managed strategy</AlertTitle>
                  Technical and risk settings may adjust within 100% verified
                  capital. {overview.aiPolicy.reason}
                  {overview.aiPolicy.lastChangedAt && (
                    <>
                      {' '}
                      Last change:{' '}
                      {TradingFormatting.time(overview.aiPolicy.lastChangedAt)}.
                    </>
                  )}
                </Alert>
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
              <Box
                component="details"
                sx={{
                  borderTop: '1px solid',
                  borderColor: 'divider',
                  '& > summary': {
                    cursor: 'pointer',
                    minHeight: 44,
                    alignContent: 'center',
                    fontSize: 14,
                    fontWeight: 600,
                    '&:focus-visible': {
                      outline: '2px solid',
                      outlineOffset: 2,
                    },
                  },
                }}
              >
                <Box component="summary">Daily reports</Box>
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
                refreshing={this.state.refreshing}
                onRetry={() => void this.load()}
                onClose={() => {
                  this.setState({ open: false, aiManaged: false });
                }}
                onDirty={() => {
                  this.setState({
                    dirty: true,
                    reviewOpen: false,
                    aiManaged: false,
                    validationError: null,
                  });
                }}
                onSave={(data) => void this.save(data)}
                onPrepareAdaptiveSetup={() =>
                  void this.prepareAdaptiveSetup(overview)
                }
                onSetMode={(mode) => void this.setMode(mode)}
              />
              {this.props.history && (
                <>
                  {(!balanceIssue || orderReviewNeeded) && (
                    <Button
                      sx={{ alignSelf: 'flex-start', mt: 1 }}
                      disabled={this.state.busy || !!this.state.error}
                      onClick={() => {
                        this.setState({ balanceReviewOpen: true });
                      }}
                    >
                      Review balances
                    </Button>
                  )}
                  <Drawer
                    anchor="right"
                    open={this.state.balanceReviewOpen}
                    onClose={() => {
                      this.setState({ balanceReviewOpen: false });
                    }}
                    slotProps={{
                      paper: {
                        role: 'dialog',
                        'aria-modal': true,
                        'aria-labelledby': 'balance-review-title',
                        sx: {
                          width: { xs: '100%', sm: 600 },
                          maxWidth: '100%',
                          p: 'var(--panel-padding)',
                          bgcolor: 'var(--surface-raised)',
                          backdropFilter: 'blur(20px)',
                          borderLeft: '1px solid',
                          borderColor: 'var(--border-panel)',
                          borderRadius:
                            'var(--radius-panel) 0 0 var(--radius-panel)',
                          boxShadow: 'var(--shadow-panel)',
                        },
                      },
                    }}
                  >
                    <Stack
                      direction="row"
                      sx={{
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        mb: 2,
                      }}
                    >
                      <Typography
                        id="balance-review-title"
                        component="h2"
                        sx={{
                          fontFamily: 'var(--font-display)',
                          fontSize: 24,
                          fontWeight: 850,
                        }}
                      >
                        Balance review
                      </Typography>
                      <IconButton
                        aria-label="Close balance review"
                        onClick={() => {
                          this.setState({ balanceReviewOpen: false });
                        }}
                        sx={{ width: 44, height: 44 }}
                      >
                        <CloseOutlinedIcon />
                      </IconButton>
                    </Stack>
                    {this.state.balanceReviewOpen && (
                      <ReconciliationPanel
                        key={overview.accountKey}
                        history={this.props.history}
                        onApplied={(kind) => {
                          this.setState({
                            actionError: null,
                            balanceReviewOpen: kind !== 'cash-baseline',
                          });
                          void this.load();
                          this.props.onBalancesChanged?.();
                        }}
                      />
                    )}
                  </Drawer>
                </>
              )}
              <TradingMandateDialog
                overview={overview}
                open={this.state.reviewOpen}
                expiresAt={this.state.expiresAt}
                busy={this.state.busy}
                dirty={this.state.dirty}
                error={this.state.error ?? this.balanceIssue}
                onClose={() => {
                  this.setState({ reviewOpen: false, aiManaged: false });
                }}
                onExpiresAtChange={(expiresAt) => {
                  this.setState({ expiresAt });
                }}
                aiManaged={this.state.aiManaged}
                onAiManagedChange={(aiManaged) => {
                  this.setState({ aiManaged });
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
          ? { open: false, reviewOpen: true, expiresAt: '', aiManaged: false }
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
      this.balanceIssue !== null ||
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
        this.state.aiManaged &&
          overview.rules.version === 'adaptive-intraday-v2',
      ),
    );
  }

  private async load(force = false): Promise<void> {
    if (this.actionInFlight && !force) {
      return;
    }

    this.clearTimer();
    this.request?.abort();
    const request = new AbortController();
    this.request = request;
    const generation = ++this.generation;
    this.setState({ refreshing: true });
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
        this.setState({ refreshing: false });
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
        actionError: changed ? null : state.actionError,
        dirty: saved || changed ? false : state.dirty,
        reviewOpen: changed || saved ? false : state.reviewOpen,
        balanceReviewOpen: changed || saved ? false : state.balanceReviewOpen,
        aiManaged: changed || saved ? false : state.aiManaged,
      };
    });
  }

  private get balanceIssue(): string | null {
    const overview = this.state.overview;
    const portfolio = this.props.portfolio;
    const observedAt = portfolio?.updatedAt
      ? Date.parse(portfolio.updatedAt)
      : NaN;
    const fresh =
      Number.isFinite(observedAt) &&
      Date.now() - observedAt <= 120000 &&
      observedAt <= Date.now() + 30000;
    const symbols = fresh
      ? (overview?.live.positions ?? [])
          .filter(
            (position) =>
              Number(position.quantity) > 0 &&
              portfolio?.positions.find(
                (holding) => holding.symbol === position.symbol,
              )?.quantity === 0,
          )
          .map((position) => position.symbol)
      : [];
    if (symbols.length > 0) {
      return `${symbols.join(', ')} holdings differ from the saved strategy. Verify sold holdings, then review available cash. Historical cost and profit remain unverified.`;
    }

    const capital = overview?.autonomy?.capital;
    if (
      overview?.mode === 'stopped' &&
      overview.mandate !== null &&
      overview.live.positions.length === 0 &&
      capital?.availableThb != null &&
      capital.blocker === null &&
      capital.reservedThb !== null &&
      ReconciliationSafety.sameAmount(capital.reservedThb, '0') &&
      !ReconciliationSafety.sameAmount(
        capital.availableThb,
        overview.live.cashThb,
      ) &&
      (overview.live.settledCashThb == null ||
        !ReconciliationSafety.sameAmount(
          capital.availableThb,
          overview.live.settledCashThb,
        ))
    ) {
      return 'Available broker cash differs from the saved strategy balance. Review the current balance before enabling.';
    }

    return null;
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
      refreshing: false,
      error: null,
      actionError: null,
      reviewOpen: false,
      aiManaged: false,
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
        const message =
          error instanceof Error
            ? error.message
            : 'Trading control failed. Refresh status before trying again.';
        this.setState({
          error: 'Trading status is being revalidated.',
          actionError: message,
        });
        await this.load(true);
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
