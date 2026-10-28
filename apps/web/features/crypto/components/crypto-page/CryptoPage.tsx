'use client';

import TradingPanel from '../../../trading/components/trading-panel/TradingPanel';
import OrderReviewPanel from '../../../orders/components/order-review-panel/OrderReviewPanel';
import { TeamActivitySnapshot } from '../../../team/presentation/TeamActivitySnapshot';

import { Component, type ReactNode } from 'react';
import {
  Box,
  Alert,
  AlertTitle,
  Button,
  Skeleton,
  Stack,
  Typography,
} from '@mui/material';
import RefreshOutlinedIcon from '@mui/icons-material/RefreshOutlined';
import AccountBalanceWalletOutlinedIcon from '@mui/icons-material/AccountBalanceWalletOutlined';
import { CryptoServiceContext } from '../../context/CryptoServiceContext';
import type { ICryptoServices } from '../../context/ICryptoServices';
import PositionFilter from '../../utils/PositionFilter';
import { PortfolioFilter } from '../../contracts/PortfolioFilter';
import ConnectionStatus from '../connection-status/ConnectionStatus';
import PortfolioSummary from '../portfolio-summary/PortfolioSummary';
import PortfolioFilters from '../portfolio-filters/PortfolioFilters';
import PortfolioTable from '../portfolio-table/PortfolioTable';
import type { CryptoPageState } from './CryptoPageState';
import FormatTime from '../../utils/FormatTime';
import { ConnectionStatusPresenter } from '../../presentation/ConnectionStatusPresenter';

export default class CryptoPage extends Component<object, CryptoPageState> {
  public static override readonly contextType = CryptoServiceContext.value;

  public override context: ICryptoServices | null = null;

  private get services(): ICryptoServices {
    return CryptoServiceContext.require(this.context);
  }

  public override state: CryptoPageState = {
    data: null,
    error: null,
    refreshing: false,
    now: 0,
    filter: PortfolioFilter.Holding,
    expanded: null,
    history: {},
    historyLoading: {},
    historyErrors: {},
  };

  private readonly requests: Set<AbortController> = new Set();

  private readonly historyRequests: Map<string, AbortController> = new Map();

  private mounted: boolean = false;

  private pollTimer: number | null = null;

  private polling: boolean = false;

  private generation: number = 0;

  private historyUpdatedAt: string | null = null;

  private activePortfolioRequest: AbortController | null = null;

  public override componentDidMount(): void {
    this.mounted = true;
    this.generation += 1;
    document.addEventListener('visibilitychange', this.visibility);
    this.updatePolling();
    const mountGeneration = this.generation;
    void Promise.resolve().then(() => {
      if (this.mounted && mountGeneration === this.generation) {
        void this.load();
      }
    });
  }

  public override componentDidUpdate(
    _previousProps: object,
    previousState: CryptoPageState,
  ): void {
    if (previousState.data?.syncing !== this.state.data?.syncing) {
      this.updatePolling();
    }

    const nextHistoryUpdatedAt = this.state.data?.historyUpdatedAt ?? null;
    if (nextHistoryUpdatedAt !== this.historyUpdatedAt) {
      this.historyUpdatedAt = nextHistoryUpdatedAt;
      this.clearHistory();
    }
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.stopPolling();
    document.removeEventListener('visibilitychange', this.visibility);
    this.activePortfolioRequest?.abort();
    for (const request of this.requests) {
      request.abort();
    }

    for (const request of this.historyRequests.values()) {
      request.abort();
    }

    this.requests.clear();
    this.historyRequests.clear();
  }

  public override render(): ReactNode {
    const { data, error, refreshing, filter, now } = this.state;
    const sourceAt = data?.updatedAt ? Date.parse(data.updatedAt) : NaN;
    const holdingsKnown = Number.isFinite(sourceAt) && sourceAt - now <= 30000;
    const positions = PositionFilter.apply(
      holdingsKnown ? (data?.positions ?? []) : [],
      filter,
    );
    const stale =
      data !== null &&
      Number.isFinite(sourceAt) &&
      now - sourceAt > ConnectionStatusPresenter.maximumSourceAgeMs;
    const unverifiedTime =
      data !== null && (!Number.isFinite(sourceAt) || sourceAt - now > 30000);

    return (
      <Stack
        spacing="var(--section-gap)"
        sx={{ width: '100%', maxWidth: 1280 }}
      >
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          spacing={2}
          sx={{
            justifyContent: 'space-between',
            alignItems: { xs: 'flex-start', sm: 'center' },
            flexWrap: 'wrap',
            gap: 2,
            p: 'var(--panel-padding)',
            bgcolor: 'var(--surface-panel)',
            backdropFilter: 'blur(var(--glass-blur))',
            border: '1px solid',
            borderColor: 'var(--border-panel)',
            borderRadius: 'var(--radius-panel)',
            boxShadow: 'var(--shadow-panel)',
          }}
        >
          <Box sx={{ minWidth: 0 }}>
            <Stack
              direction="row"
              spacing={1}
              sx={{ alignItems: 'center', flexWrap: 'wrap' }}
            >
              <Box
                aria-hidden="true"
                sx={{
                  width: 48,
                  height: 48,
                  display: 'grid',
                  placeItems: 'center',
                  bgcolor: 'var(--color-mint)',
                  color: 'text.primary',
                  borderRadius: 'var(--radius-control)',
                  flexShrink: 0,
                }}
              >
                <AccountBalanceWalletOutlinedIcon sx={{ fontSize: 28 }} />
              </Box>
              <Typography
                component="h1"
                sx={{
                  fontFamily: 'var(--font-display)',
                  fontSize: 34,
                  fontWeight: 600,
                  lineHeight: 1.13,
                  m: 0,
                  minWidth: 0,
                  overflowWrap: 'break-word',
                }}
              >
                Crypto
              </Typography>
              <ConnectionStatus
                data={data}
                error={error}
                refreshing={refreshing}
                now={now}
              />
            </Stack>
            <Typography sx={{ mt: 1, fontSize: 14, color: 'text.secondary' }}>
              Bitkub portfolio
              {data
                ? ` · ${String(data.positions.length)} ${data.positions.length === 1 ? 'asset' : 'assets'} tracked`
                : ''}
            </Typography>
          </Box>
          <Button
            variant="outlined"
            startIcon={<RefreshOutlinedIcon />}
            onClick={this.handleRefresh}
            disabled={refreshing}
            sx={{
              borderColor: 'var(--border-panel)',
              color: 'text.primary',
              bgcolor: 'var(--surface-raised)',
              minHeight: 44,
              px: 2.5,
              textTransform: 'none',
              flexShrink: 0,
            }}
          >
            {refreshing ? 'Refreshing' : 'Refresh'}
          </Button>
        </Stack>
        {error && (
          <Alert
            severity="error"
            aria-busy={refreshing}
          >
            <AlertTitle>
              {data ? 'Snapshot could not refresh' : 'Portfolio unavailable'}
            </AlertTitle>
            <Typography variant="body2">
              {data ? 'Showing the last saved snapshot. ' : ''}
              {error}
            </Typography>
            <Button
              variant="outlined"
              color="inherit"
              onClick={this.handleRefresh}
              disabled={refreshing}
              sx={{ mt: 1.5 }}
            >
              {refreshing ? 'Retrying…' : 'Retry'}
            </Button>
          </Alert>
        )}
        {!data && !error && (
          <Box
            role="status"
            aria-label="Loading portfolio"
            sx={{
              display: 'grid',
              gridTemplateColumns: 'repeat(3, 1fr)',
              gap: 2,
            }}
          >
            {[0, 1, 2].map((item) => (
              <Skeleton
                key={item}
                variant="rounded"
                height={100}
              />
            ))}
          </Box>
        )}
        {data && (
          <PortfolioSummary
            data={data}
            holdingsKnown={holdingsKnown}
          />
        )}
        {data && (
          <Box
            component="dl"
            aria-label="Source update times"
            sx={{
              m: 0,
              display: 'grid',
              gridTemplateColumns: {
                xs: 'repeat(2, minmax(0, 1fr))',
                lg: 'repeat(4, minmax(0, 1fr))',
              },
              columnGap: 2,
              rowGap: 1,
              color: 'text.secondary',
              '& dt': { fontSize: 12, fontWeight: 800 },
              '& dd': { m: 0, fontSize: 12, overflowWrap: 'anywhere' },
            }}
          >
            <Box>
              <Box component="dt">Snapshot updated</Box>
              <Box component="dd">{FormatTime.format(data.updatedAt)}</Box>
            </Box>
            <Box>
              <Box component="dt">Prices</Box>
              <Box component="dd">{FormatTime.format(data.priceUpdatedAt)}</Box>
            </Box>
            <Box>
              <Box component="dt">History</Box>
              <Box component="dd">
                {FormatTime.format(data.historyUpdatedAt)}
              </Box>
            </Box>
            <Box>
              <Box component="dt">Timezone</Box>
              <Box component="dd">Bangkok</Box>
            </Box>
          </Box>
        )}
        {stale && !error && (
          <Alert severity="warning">
            Snapshot is older than 2 minutes. Refresh reads saved data; it does
            not start a broker import.
          </Alert>
        )}
        {unverifiedTime && !error && (
          <Alert severity="warning">
            The source snapshot timestamp is unavailable or cannot be verified.
          </Alert>
        )}
        <Stack
          id="trading"
          spacing={2}
        >
          <TradingPanel
            date={TeamActivitySnapshot.today()}
            portfolio={data}
            history={this.services.history}
            onBalancesChanged={this.handleLoad}
          />
          <OrderReviewPanel />
        </Stack>
        {data && (
          <>
            <Box
              id="data-scope"
              component="details"
              aria-label="Wallet data scope"
              sx={{
                fontSize: 12,
                color: 'text.secondary',
                p: 'var(--panel-padding)',
                bgcolor: 'var(--surface-soft)',
                borderRadius: 'var(--radius-panel)',
                '& p': { m: 0, mb: 0.5 },
                '& > summary': {
                  minHeight: 44,
                  alignContent: 'center',
                  cursor: 'pointer',
                  color: 'text.primary',
                  fontSize: 14,
                  fontWeight: 600,
                  '&:focus-visible': { outline: '2px solid', outlineOffset: 2 },
                },
              }}
            >
              <Box component="summary">Data details</Box>
              <Stack
                direction={{ xs: 'column', sm: 'row' }}
                spacing={{ xs: 0.5, sm: 2 }}
                sx={{ mb: 0.5 }}
              >
                <Typography
                  variant="body2"
                  sx={{ fontWeight: 750 }}
                >
                  Wallet holdings
                </Typography>
                <Typography variant="body2">Bitkub TH</Typography>
              </Stack>
              <Typography variant="body2">
                Latest snapshot, independent of the trading report date.
              </Typography>
              <Typography variant="body2">
                Snapshot updated is the saved observation time and may come from
                a newer capital observation. It does not confirm a new full
                broker wallet import. Refresh only rereads this API; backend
                imports update source records separately.
              </Typography>
              <Typography variant="body2">{data.historyCoverage}</Typography>
              <Typography
                variant="caption"
                component="p"
              >
                Crypto value excludes THB. Available THB excludes
                broker-reserved cash; pending local proposals may reduce
                spendable capital further. Sources can update at different
                times.
              </Typography>
              <Typography
                variant="caption"
                component="p"
              >
                This wallet snapshot does not identify its account. Engine last
                buy and P/L cover confirmed live engine fills only;
                whole-account cost and profit remain unknown when history or
                transfers are incomplete.
              </Typography>
            </Box>
            <Typography
              component="h2"
              sx={{
                fontFamily: 'var(--font-display)',
                fontSize: 24,
                fontWeight: 850,
              }}
            >
              Assets
            </Typography>
            <PortfolioFilters
              filter={filter}
              onChange={this.handleFilterChange}
            />
            <PortfolioTable
              positions={positions}
              hasRecords={data.positions.length > 0}
              holdingsKnown={holdingsKnown}
              filter={filter}
              expanded={this.state.expanded}
              onOpenHistory={this.handleOpenHistory}
              history={this.state.history}
              historyLoading={this.state.historyLoading}
              historyErrors={this.state.historyErrors}
              onRetry={this.handleRetryHistory}
            />
          </>
        )}
      </Stack>
    );
  }

  private readonly handleLoad = (): void => {
    void this.load();
  };

  private readonly handleRefresh = (): void => {
    void this.refresh();
  };

  private readonly handleFilterChange = (filter: PortfolioFilter): void => {
    this.setState({ filter });
  };

  private readonly handleOpenHistory = (symbol: string): void => {
    void this.openHistory(symbol, false);
  };

  private readonly handleRetryHistory = (symbol: string): void => {
    void this.openHistory(symbol, true);
  };

  private async load(manual: boolean = false): Promise<void> {
    if (!this.mounted) {
      return;
    }

    this.activePortfolioRequest?.abort();
    const controller = new AbortController();
    const requestGeneration = this.generation;
    this.activePortfolioRequest = controller;
    this.requests.add(controller);
    this.setState({ now: Date.now(), refreshing: manual });
    const timeout = window.setTimeout(() => {
      controller.abort();
    }, 8000);
    try {
      const next = await this.services.portfolio.get(controller.signal);
      if (this.isPortfolioCurrent(controller, requestGeneration)) {
        if (controller.signal.aborted) {
          throw new Error('Portfolio request timed out.');
        }

        if (next.updatedAt && Date.parse(next.updatedAt) - Date.now() > 30000) {
          throw new Error(
            'The snapshot timestamp is in the future. Its freshness cannot be verified.',
          );
        }

        const previousAt = this.state.data?.updatedAt;
        if (
          previousAt &&
          next.updatedAt &&
          Date.parse(next.updatedAt) < Date.parse(previousAt)
        ) {
          throw new Error(
            'An older snapshot was received. The last verified snapshot is retained.',
          );
        }

        this.setState({ data: next, error: next.error, now: Date.now() });
      }
    } catch (cause) {
      if (this.isPortfolioCurrent(controller, requestGeneration)) {
        this.setState({
          error: controller.signal.aborted
            ? 'Portfolio request timed out.'
            : this.message(cause, 'Unable to load portfolio.'),
          now: Date.now(),
        });
      }
    } finally {
      window.clearTimeout(timeout);
      this.requests.delete(controller);
      if (this.isPortfolioCurrent(controller, requestGeneration)) {
        this.setState({ refreshing: false });
        this.activePortfolioRequest = null;
      }
    }
  }

  private async refresh(): Promise<void> {
    if (!this.mounted || this.state.refreshing) {
      return;
    }

    await this.load(true);
  }

  private updatePolling(): void {
    this.stopPolling();
    if (this.mounted && !document.hidden) {
      this.startPolling();
    }
  }

  private startPolling(): void {
    if (this.pollTimer !== null) {
      return;
    }

    this.pollTimer = window.setInterval(
      () => {
        if (!this.mounted || document.hidden) {
          return;
        }

        this.setState({ now: Date.now() });
        if (this.polling || this.activePortfolioRequest) {
          return;
        }

        this.polling = true;
        void this.load().finally(() => {
          this.polling = false;
        });
      },
      this.state.data?.syncing ? 2000 : 15000,
    );
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      window.clearInterval(this.pollTimer);
      this.pollTimer = null;
    }

    this.polling = false;
  }

  private readonly visibility = (): void => {
    this.stopPolling();
    if (document.hidden) {
      const request = this.activePortfolioRequest;
      this.activePortfolioRequest = null;
      request?.abort();
      this.setState({ refreshing: false });
    } else {
      this.setState({ now: Date.now() });
      this.startPolling();
      void this.load();
    }
  };

  private isPortfolioCurrent(
    controller: AbortController,
    requestGeneration: number,
  ): boolean {
    return (
      this.mounted &&
      requestGeneration === this.generation &&
      this.activePortfolioRequest === controller
    );
  }

  private clearHistory(): void {
    for (const request of this.historyRequests.values()) {
      request.abort();
    }

    this.historyRequests.clear();
    if (this.mounted) {
      this.setState({
        history: {},
        historyErrors: {},
        historyLoading: {},
        expanded: null,
      });
    }
  }

  private async openHistory(symbol: string, force: boolean): Promise<void> {
    const expanded = this.state.expanded === symbol ? null : symbol;
    this.setState({ expanded: force ? symbol : expanded });
    if (
      !force &&
      (expanded === null ||
        Object.hasOwn(this.state.history, symbol) ||
        this.historyRequests.has(symbol))
    ) {
      return;
    }

    this.historyRequests.get(symbol)?.abort();
    const controller = new AbortController();
    const requestGeneration = this.generation;
    this.requests.add(controller);
    this.historyRequests.set(symbol, controller);
    this.setState((current) => ({
      historyLoading: { ...current.historyLoading, [symbol]: true },
      historyErrors: Object.fromEntries(
        Object.entries(current.historyErrors).filter(([key]) => key !== symbol),
      ),
    }));
    let timedOut = false;
    const timeout = window.setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, 8000);
    try {
      const response = await this.services.history.get(
        symbol,
        controller.signal,
      );
      if (this.isHistoryCurrent(symbol, controller, requestGeneration)) {
        if (controller.signal.aborted) {
          throw new Error('History request timed out. Try again.');
        }

        this.setState((current) => ({
          history: { ...current.history, [symbol]: response.trades },
        }));
      }
    } catch (cause) {
      if (this.isHistoryCurrent(symbol, controller, requestGeneration)) {
        this.setState((current) => ({
          historyErrors: {
            ...current.historyErrors,
            [symbol]: timedOut
              ? 'History request timed out. Try again.'
              : this.message(cause, 'Unable to load history.'),
          },
        }));
      }
    } finally {
      window.clearTimeout(timeout);
      this.requests.delete(controller);
      const current = this.historyRequests.get(symbol) === controller;
      if (current) {
        this.historyRequests.delete(symbol);
      }

      if (current && this.mounted && requestGeneration === this.generation) {
        this.setState((current) => ({
          historyLoading: { ...current.historyLoading, [symbol]: false },
        }));
      }
    }
  }

  private isHistoryCurrent(
    symbol: string,
    controller: AbortController,
    requestGeneration: number,
  ): boolean {
    return (
      this.mounted &&
      requestGeneration === this.generation &&
      this.historyRequests.get(symbol) === controller
    );
  }

  private message(cause: unknown, fallback: string): string {
    return cause instanceof Error ? cause.message : fallback;
  }
}
