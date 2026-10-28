'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  Button,
  CircularProgress,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  Typography,
} from '@mui/material';
import { TradingServiceContext } from '../../context/TradingServiceContext';
import { ReconciliationSafety } from '../../presentation/ReconciliationSafety';
import TradingFormatting from '../../presentation/TradingFormatting';
import ReconciliationReview from '../reconciliation-review/ReconciliationReview';
import type { ReconciliationPanelProps } from './ReconciliationPanelProps';
import type { ReconciliationPanelState } from './ReconciliationPanelState';
import type { ReconciliationPreview } from '../../contracts/ReconciliationPreview';
import { TradeSide } from '../../../crypto/contracts/TradeSide';

export default class ReconciliationPanel extends Component<
  ReconciliationPanelProps,
  ReconciliationPanelState
> {
  public static override readonly contextType = TradingServiceContext.value;

  declare public context: ContextType<typeof TradingServiceContext.value>;

  public override state: ReconciliationPanelState = {
    overview: null,
    symbol: '',
    executionId: '',
    trades: [],
    preview: null,
    acknowledged: false,
    uncertain: false,
    unreadableAccount: null,
    busy: false,
    now: 0,
    error: null,
    notice: null,
  };

  private mounted = false;

  private inFlight = false;

  private request: AbortController | null = null;

  private generation = 0;

  private timer: number | null = null;

  private initialReviewStage: 'status' | 'history' | 'done' = 'status';

  private initialReviewAccount: string | null = null;

  public override componentDidMount(): void {
    this.mounted = true;
    this.setState({ now: Date.now() });
    this.timer = window.setInterval(() => {
      this.setState({ now: Date.now() });
    }, 1000);
    void this.loadStatus();
  }

  public override componentDidUpdate(): void {
    this.prepareInitialPreview();
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.inFlight = false;
    this.request?.abort();
    if (this.timer !== null) {
      window.clearInterval(this.timer);
    }
  }

  private get services() {
    return TradingServiceContext.require(this.context);
  }

  public override render(): ReactNode {
    const {
      overview,
      busy,
      preview,
      uncertain,
      symbol,
      executionId,
      error,
      notice,
    } = this.state;
    const stopped = overview !== null && ReconciliationSafety.stopped(overview);
    const activeOrders =
      overview !== null && ReconciliationSafety.unresolvedOrders(overview);
    const needsStopping =
      overview !== null &&
      (overview.mode !== 'stopped' ||
        (overview.mandate !== null && overview.mandate.revokedAt === null));
    const locked =
      busy ||
      preview !== null ||
      this.state.unreadableAccount !== null ||
      !stopped;
    let reviewDescription =
      'Review trades made directly in Bitkub and update saved balances.';
    if (uncertain) {
      reviewDescription =
        'Check the result of your previous balance confirmation.';
    } else if (stopped) {
      reviewDescription = 'Trading stays off during review.';
    }

    const lots =
      overview?.live.positions.filter(
        (position) => Number(position.quantity) > 0,
      ) ?? [];

    return (
      <Stack
        component="section"
        aria-label="Balance review steps"
        spacing={2}
        sx={{
          '& h3': { fontFamily: 'var(--font-display)', fontWeight: 850 },
          '& .MuiFormControl-root': {
            bgcolor: 'var(--surface-raised)',
            borderRadius: 'var(--radius-control)',
          },
        }}
      >
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          spacing={1}
          sx={{
            justifyContent: 'space-between',
            alignItems: { sm: 'center' },
          }}
        >
          <Typography
            variant="body2"
            color="text.secondary"
          >
            {reviewDescription}
          </Typography>
          <Button
            disabled={busy}
            onClick={() => void this.loadStatus()}
            sx={{ minHeight: 44, textTransform: 'none' }}
          >
            Refresh status
          </Button>
        </Stack>
        {overview &&
          (!preview ||
            uncertain ||
            preview.binding.accountKey !== overview.accountKey) && (
            <Typography variant="body2">
              Bitkub account …{overview.accountKey.slice(-8)} ·{' '}
              {this.statusMessage(overview, stopped, activeOrders)}
            </Typography>
          )}
        {!preview && (busy || (!overview && !error)) && (
          <Stack
            role="status"
            direction="row"
            spacing={1}
            sx={{
              alignItems: 'center',
              color: 'text.secondary',
              bgcolor: 'var(--surface-soft)',
              borderRadius: 'var(--radius-control)',
              p: 1.5,
            }}
          >
            {busy && (
              <CircularProgress
                size={18}
                color="inherit"
                aria-hidden
              />
            )}
            <Typography variant="body2">
              {busy
                ? 'Updating balance review…'
                : 'Refresh status to continue.'}
            </Typography>
          </Stack>
        )}
        {error && (
          <Alert
            severity={
              this.state.unreadableAccount !== null ? 'warning' : 'error'
            }
          >
            {error}
          </Alert>
        )}
        {this.state.unreadableAccount !== null && (
          <Button
            variant="outlined"
            disabled={busy}
            onClick={this.discard}
            sx={{ alignSelf: 'flex-start', minHeight: 44 }}
          >
            Discard saved confirmation
          </Button>
        )}
        {notice && <Alert severity="info">{notice}</Alert>}
        {needsStopping && !activeOrders && !uncertain && (
          <Button
            variant="contained"
            disabled={busy}
            onClick={() => void this.stopForReview()}
            sx={{
              alignSelf: 'flex-start',
              minHeight: 44,
              textTransform: 'none',
            }}
          >
            Pause trading to review
          </Button>
        )}
        {!preview &&
          stopped &&
          this.state.unreadableAccount === null &&
          lots.length > 0 && (
            <>
              <Typography
                component="h3"
                sx={{ fontSize: 16, fontWeight: 650 }}
              >
                1. Review sold holdings
              </Typography>
              <Stack
                direction={{ xs: 'column', sm: 'row' }}
                spacing={1.5}
              >
                <FormControl
                  sx={{ flex: 1 }}
                  disabled={locked}
                >
                  <InputLabel id="reconciliation-symbol-label">
                    Strategy lot
                  </InputLabel>
                  <Select
                    labelId="reconciliation-symbol-label"
                    label="Strategy lot"
                    value={symbol}
                    onChange={(event) => {
                      this.setState({
                        symbol: event.target.value,
                        executionId: '',
                        trades: [],
                        preview: null,
                        acknowledged: false,
                      });
                    }}
                  >
                    {lots.map((lot) => (
                      <MenuItem
                        key={lot.symbol}
                        value={lot.symbol}
                      >
                        {lot.symbol} · {lot.quantity}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
                <Button
                  variant="outlined"
                  disabled={locked || !symbol}
                  onClick={() => void this.loadHistory()}
                  sx={{ minHeight: 44, textTransform: 'none' }}
                >
                  Find matching sales
                </Button>
              </Stack>
              {this.state.trades.length > 0 && (
                <FormControl disabled={locked}>
                  <InputLabel id="reconciliation-sale-label">
                    Imported sell to verify
                  </InputLabel>
                  <Select
                    labelId="reconciliation-sale-label"
                    label="Imported sell to verify"
                    value={executionId}
                    onChange={(event) => {
                      this.setState({
                        executionId: event.target.value,
                        preview: null,
                        acknowledged: false,
                      });
                    }}
                  >
                    {this.state.trades.map((trade) => (
                      <MenuItem
                        key={trade.transactionId}
                        value={trade.transactionId}
                      >
                        {trade.symbol} ·{' '}
                        {TradingFormatting.time(trade.timestamp)} ·{' '}
                        {trade.transactionId.slice(-8)}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
              )}
              <Button
                variant="outlined"
                disabled={locked || !symbol || !executionId}
                onClick={() => void this.preview('manual-sale')}
                sx={{
                  alignSelf: 'flex-start',
                  minHeight: 44,
                  textTransform: 'none',
                }}
              >
                Verify sold holdings
              </Button>
            </>
          )}
        {!preview &&
          stopped &&
          this.state.unreadableAccount === null &&
          lots.length === 0 && (
            <>
              <Typography
                component="h3"
                sx={{ fontSize: 16, fontWeight: 650 }}
              >
                2. Review available cash
              </Typography>
              <Typography variant="body2">
                Use verified broker cash after sold holdings and reservations
                are resolved. This replaces executable cash once; it is not new
                funding or a claimed sale settlement.
              </Typography>
              <Button
                variant="outlined"
                disabled={locked}
                onClick={() => void this.preview('cash-baseline')}
                sx={{
                  alignSelf: 'flex-start',
                  minHeight: 44,
                  textTransform: 'none',
                }}
              >
                Preview current cash
              </Button>
            </>
          )}
        {preview && (
          <ReconciliationReview
            preview={preview}
            acknowledged={this.state.acknowledged}
            busy={busy}
            blocked={
              overview === null ||
              !ReconciliationSafety.current(preview, overview, uncertain)
            }
            expired={!ReconciliationSafety.usable(preview, this.state.now)}
            uncertain={uncertain}
            onAcknowledge={(checked) => {
              this.setState({ acknowledged: checked });
            }}
            onConfirm={() => void this.confirm()}
            onDiscard={this.discard}
          />
        )}
      </Stack>
    );
  }

  private async run(
    action: (signal: AbortSignal) => Promise<void>,
  ): Promise<boolean> {
    if (this.inFlight) {
      return false;
    }

    this.inFlight = true;
    const generation = ++this.generation;
    const request = new AbortController();
    this.request = request;
    this.setState({ busy: true, error: null });
    try {
      await action(
        AbortSignal.any([request.signal, AbortSignal.timeout(35000)]),
      );

      return true;
    } catch (error) {
      if (this.mounted && generation === this.generation) {
        this.setState({
          error:
            error instanceof Error
              ? error.message
              : 'Unable to complete this request. No automatic retry was made.',
          acknowledged: false,
        });
      }

      return false;
    } finally {
      if (generation === this.generation) {
        this.inFlight = false;
        if (this.mounted) {
          this.setState({ busy: false });
        }
      }
    }
  }

  private active(signal: AbortSignal): boolean {
    return this.mounted && !signal.aborted;
  }

  private statusMessage(
    overview: NonNullable<ReconciliationPanelState['overview']>,
    stopped: boolean,
    activeOrders: boolean,
  ): string {
    if (stopped) {
      return 'Stopped · mandate revoked';
    }

    if (activeOrders) {
      return 'Open or unresolved orders need review. Check Orders & positions before updating balances.';
    }

    if (overview.mandate === null) {
      return 'No previous live trading setup is recorded for this account.';
    }

    return 'Pause trading to check your Bitkub sales. Existing orders and history stay unchanged.';
  }

  private async loadStatus(): Promise<void> {
    await this.run(async (signal) => {
      const overview = await this.services
        .get(signal)
        .catch((error: unknown) => {
          if (this.active(signal)) {
            this.setState({ overview: null, acknowledged: false });
          }

          throw error;
        });
      if (!this.active(signal)) {
        return;
      }

      let preview = this.state.preview;
      let uncertain = this.state.uncertain;
      const accountChanged =
        this.state.overview?.accountKey !== overview.accountKey;
      if (accountChanged || preview === null) {
        try {
          preview = this.services.pendingReconciliation(overview.accountKey);
        } catch {
          this.setState({
            overview,
            preview: null,
            acknowledged: false,
            uncertain: true,
            unreadableAccount: overview.accountKey,
            symbol: '',
            executionId: '',
            trades: [],
            error:
              'Saved confirmation could not be read. It may already be recorded on the server. Check current status before discarding the saved copy.',
          });

          return;
        }

        uncertain = preview !== null;
      }

      if (
        preview &&
        !ReconciliationSafety.current(preview, overview, uncertain)
      ) {
        if (!uncertain) {
          preview = null;
        }

        this.setState({
          notice:
            'Account, rules or material state changed. The old preview cannot be confirmed. Any saved uncertain confirmation remains recorded in this tab.',
        });
      }

      const lots = overview.live.positions.filter(
        (position) => Number(position.quantity) > 0,
      );
      let symbol = accountChanged ? '' : this.state.symbol;
      if (!lots.some((lot) => lot.symbol === symbol)) {
        symbol = lots.length === 1 ? (lots[0]?.symbol ?? '') : '';
      }

      const selectionChanged = accountChanged || symbol !== this.state.symbol;
      this.setState({
        overview,
        preview,
        unreadableAccount: null,
        uncertain: preview !== null && uncertain,
        acknowledged: false,
        symbol,
        executionId: selectionChanged ? '' : this.state.executionId,
        trades: selectionChanged ? [] : this.state.trades,
      });
    });
  }

  private async stopForReview(): Promise<void> {
    const displayed = this.state.overview;
    if (!displayed || ReconciliationSafety.stopped(displayed)) {
      return;
    }

    const disabled = await this.run(async (signal) => {
      const current = await this.services.get(signal);
      if (!this.active(signal)) {
        return;
      }

      if (
        current.accountKey !== displayed.accountKey ||
        current.rulesHash !== displayed.rulesHash
      ) {
        throw new Error(
          'Account or trading rules changed. Reload reconciliation status before stopping.',
        );
      }

      if (ReconciliationSafety.unresolvedOrders(current)) {
        throw new Error(
          'Open or unresolved orders need review. Check Orders & positions before updating balances.',
        );
      }

      await this.services.disable(signal);
    });

    if (disabled && this.mounted) {
      this.initialReviewStage = 'status';
      await this.loadStatus();
    }
  }

  private prepareInitialPreview(): void {
    if (
      this.initialReviewStage === 'done' ||
      this.state.unreadableAccount !== null ||
      !this.mounted ||
      this.inFlight ||
      this.state.busy
    ) {
      return;
    }

    const overview = this.state.overview;
    if (!overview && !this.state.error) {
      return;
    }

    if (
      !overview ||
      !ReconciliationSafety.stopped(overview) ||
      this.state.preview ||
      this.state.uncertain ||
      this.state.error
    ) {
      this.initialReviewStage = 'done';

      return;
    }

    const stage = this.initialReviewStage;
    this.initialReviewStage = 'done';
    if (stage === 'history') {
      if (
        overview.accountKey === this.initialReviewAccount &&
        this.state.trades.length === 1 &&
        this.state.executionId !== ''
      ) {
        void this.preview('manual-sale');
      }

      return;
    }

    const lots = overview.live.positions.filter(
      (position) => Number(position.quantity) > 0,
    );
    if (lots.length === 0) {
      void this.preview('cash-baseline');

      return;
    }

    if (lots.length === 1) {
      this.initialReviewAccount = overview.accountKey;
      this.initialReviewStage = 'history';
      void this.loadHistory();
    }
  }

  private async loadHistory(): Promise<void> {
    const symbol = this.state.symbol;
    const accountKey = this.state.overview?.accountKey;
    const lot = this.state.overview?.live.positions.find(
      (position) => position.symbol === symbol,
    );
    if (!lot) {
      return;
    }

    await this.run(async (signal) => {
      const result = await this.props.history.get(symbol, signal);
      if (
        !this.active(signal) ||
        this.state.symbol !== symbol ||
        (accountKey && this.state.overview?.accountKey !== accountKey)
      ) {
        return;
      }

      const trades = result.trades.filter(
        (trade) =>
          trade.symbol === symbol &&
          trade.side === TradeSide.Sell &&
          Date.parse(trade.timestamp) >= Date.parse(lot.openedAt),
      );
      this.setState({
        trades,
        executionId:
          trades.length === 1 ? (trades[0]?.transactionId ?? '') : '',
        notice: trades.length
          ? null
          : 'No matching sell was found in imported history. Check the broker trade history before continuing.',
      });
    });
  }

  private async preview(kind: ReconciliationPreview['kind']): Promise<void> {
    if (this.state.unreadableAccount !== null) {
      return;
    }

    await this.run(async (signal) => {
      const overview = await this.services.get(signal);
      if (
        !ReconciliationSafety.stopped(overview) ||
        (this.state.overview &&
          overview.accountKey !== this.state.overview.accountKey)
      ) {
        throw new Error(
          'Account or trading authority changed. Reload reconciliation status.',
        );
      }

      const binding = ReconciliationSafety.binding(overview, this.state.symbol);
      let preview: ReconciliationPreview;
      if (kind === 'manual-sale') {
        if (!binding.originalLot) {
          throw new Error('The selected strategy lot changed. Reload status.');
        }

        const result = await this.services.previewManualSale(
          this.state.symbol,
          this.state.executionId,
          signal,
        );
        if (
          result.symbol !== this.state.symbol ||
          result.brokerBalance.symbol !== result.symbol ||
          Number(result.brokerBalance.quantity) !== 0 ||
          Number(result.brokerBalance.available) !== 0 ||
          result.brokerBalance.available === null
        ) {
          throw new Error(
            'Preview does not verify empty inventory for this selected symbol.',
          );
        }

        preview = { kind, result, binding };
      } else {
        const result = await this.services.previewCashBaseline(signal);
        if (
          result.accountKey !== overview.accountKey ||
          !ReconciliationSafety.sameAmount(result.cashThb, result.availableThb)
        ) {
          throw new Error(
            'Cash preview account or reserved balance does not match.',
          );
        }

        preview = { kind, result, binding };
      }

      const current = await this.services.get(signal);
      if (!this.active(signal)) {
        return;
      }

      if (
        preview.result.applied ||
        !ReconciliationSafety.current(preview, current, false) ||
        !ReconciliationSafety.usable(preview, Date.now()) ||
        Date.now() - Date.parse(preview.result.brokerObservedAt) > 30000
      ) {
        throw new Error(
          'Preview is stale or the account state changed. Request a fresh preview.',
        );
      }

      this.setState({
        overview: current,
        preview,
        acknowledged: false,
        uncertain: false,
        notice: null,
        now: Date.now(),
      });
    });
  }

  private async confirm(): Promise<void> {
    const preview = this.state.preview;
    const uncertain = this.state.uncertain;
    if (
      !preview ||
      !this.state.acknowledged ||
      (!uncertain && !ReconciliationSafety.usable(preview, Date.now()))
    ) {
      return;
    }

    await this.run(async (signal) => {
      const current = await this.services.get(signal);
      if (!this.active(signal)) {
        return;
      }

      if (!ReconciliationSafety.current(preview, current, uncertain)) {
        throw new Error(
          'Account, rules or material state changed. Reload status and review again.',
        );
      }

      if (!uncertain && !ReconciliationSafety.usable(preview, Date.now())) {
        throw new Error(
          'The preview expired while checking status. Request a new preview.',
        );
      }

      this.services.retainReconciliation(preview);
      this.setState({ uncertain: true, acknowledged: false });
      const result =
        preview.kind === 'manual-sale'
          ? await this.services.applyManualSale(
              preview.result.proof,
              preview.result.acknowledgement,
              signal,
            )
          : await this.services.applyCashBaseline(
              preview.result.proof,
              preview.result.acknowledgement,
              signal,
            );
      if (!result.applied || result.proof !== preview.result.proof) {
        throw new Error(
          'The server did not confirm this exact review. Outcome remains unknown.',
        );
      }

      this.services.clearReconciliation(
        preview.binding.accountKey,
        preview.result.proof,
      );
      if (!this.active(signal)) {
        return;
      }

      this.setState({
        preview: null,
        uncertain: false,
        notice:
          'The server confirmed this accounting change. Trading was not enabled.',
      });
      this.props.onApplied(preview.kind);
      const overview = await this.services.get(signal);
      if (this.active(signal)) {
        const lots = overview.live.positions.filter(
          (position) => Number(position.quantity) > 0,
        );
        if (
          preview.kind === 'manual-sale' &&
          overview.accountKey === preview.binding.accountKey
        ) {
          this.initialReviewAccount = overview.accountKey;
          this.initialReviewStage = 'status';
        }

        this.setState({
          overview,
          symbol: lots.length === 1 ? (lots[0]?.symbol ?? '') : '',
          executionId: '',
          trades: [],
        });
      }
    });
  }

  private readonly discard = (): void => {
    const preview = this.state.preview;
    const account = this.state.unreadableAccount ?? preview?.binding.accountKey;
    if (account) {
      try {
        this.services.clearReconciliation(account);
      } catch {
        this.setState({
          error:
            'Unable to discard the saved confirmation. The saved copy remains in this tab.',
        });

        return;
      }
    }

    this.setState({
      overview:
        this.state.unreadableAccount !== null ? null : this.state.overview,
      preview: null,
      unreadableAccount: null,
      acknowledged: false,
      uncertain: false,
      error: null,
      notice: this.state.uncertain
        ? 'Saved confirmation discarded locally. This does not undo or confirm any server change. Reload status before another review.'
        : null,
    });
  };
}
