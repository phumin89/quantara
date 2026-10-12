'use client';

import { Component, type ReactNode } from 'react';
import {
  Box,
  Button,
  CircularProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import RefreshOutlinedIcon from '@mui/icons-material/RefreshOutlined';
import { RequestService } from '../../../../shared/services/RequestService';
import { PortfolioService } from '../../services/PortfolioService';
import { HistoryService } from '../../services/HistoryService';
import PositionFilter from '../../utils/PositionFilter';
import { PortfolioFilter } from '../../contracts/PortfolioFilter';
import ConnectionStatus from '../connection-status/ConnectionStatus';
import PortfolioSummary from '../portfolio-summary/PortfolioSummary';
import PortfolioFilters from '../portfolio-filters/PortfolioFilters';
import PortfolioTable from '../portfolio-table/PortfolioTable';
import type { CryptoPageState } from './CryptoPageState';

export default class CryptoPage extends Component<object, CryptoPageState> {
  private readonly requestService = new RequestService();

  private readonly portfolioService = new PortfolioService(this.requestService);

  private readonly historyService = new HistoryService(this.requestService);

  public override state: CryptoPageState = {
    data: null,
    error: null,
    refreshing: false,
    filter: PortfolioFilter.All,
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
    const { data, error, refreshing, filter } = this.state;
    const positions = PositionFilter.apply(data?.positions ?? [], filter);

    return (
      <Stack
        spacing={4}
        sx={{ width: '100%', maxWidth: 1280 }}
      >
        <Stack
          direction="row"
          spacing={2}
          sx={{ justifyContent: 'space-between', alignItems: 'center' }}
        >
          <Box sx={{ minWidth: 0 }}>
            <Stack
              direction="row"
              spacing={1}
              sx={{ alignItems: 'center', flexWrap: 'wrap' }}
            >
              <Typography
                component="h1"
                sx={{ fontSize: 32, fontWeight: 650, lineHeight: 1.13, m: 0 }}
              >
                Crypto
              </Typography>
              <ConnectionStatus
                data={data}
                error={error}
                refreshing={refreshing}
              />
            </Stack>
            <Typography sx={{ mt: 1, fontSize: 14, color: '#707070' }}>
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
            disabled={refreshing || data?.syncing}
            sx={{
              borderColor: '#e0e0e0',
              color: '#141414',
              minHeight: 44,
              px: 2.5,
              textTransform: 'none',
              flexShrink: 0,
            }}
          >
            {refreshing ? 'Syncing…' : 'Refresh'}
          </Button>
        </Stack>
        {error && data && (
          <Paper
            variant="outlined"
            sx={{ p: 2, borderColor: '#fecaca', bgcolor: '#fef2f2' }}
          >
            <Typography variant="body2">
              Showing the latest saved snapshot. {error}
            </Typography>
            <Button
              variant="outlined"
              onClick={this.handleRefresh}
              disabled={refreshing || data.syncing}
              sx={{ mt: 1, minHeight: 44, borderRadius: '6px' }}
            >
              Retry
            </Button>
          </Paper>
        )}
        {!data && !error && (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
            <CircularProgress size={24} />
          </Box>
        )}
        {!data && error && (
          <Stack
            spacing={2}
            sx={{ alignItems: 'flex-start' }}
          >
            <Typography sx={{ color: 'error.main' }}>{error}</Typography>
            <Button
              variant="outlined"
              onClick={this.handleLoad}
              sx={{ borderRadius: '6px' }}
            >
              Retry
            </Button>
          </Stack>
        )}
        {data && (
          <>
            <PortfolioSummary data={data} />
            <PortfolioFilters
              filter={filter}
              onChange={this.handleFilterChange}
            />
            <Box sx={{ color: '#707070', fontSize: 14 }}>
              <Typography
                variant="body2"
                sx={{
                  fontSize: 14,
                  color: 'warning.main',
                  bgcolor: 'warning.light',
                  px: 1.5,
                  py: 1,
                  borderRadius: '6px',
                }}
              >
                90-day exchange history · Cost &amp; P/L unverified · Broker
                conversions excluded
              </Typography>
            </Box>
            <PortfolioTable
              positions={positions}
              hasRecords={data.positions.length > 0}
              expanded={this.state.expanded}
              onOpenHistory={this.handleOpenHistory}
              history={this.state.history}
              historyLoading={this.state.historyLoading}
              historyErrors={this.state.historyErrors}
              onRetry={this.handleRetryHistory}
            />
            <Box
              component="details"
              sx={{ color: '#707070', fontSize: 14, mt: -1 }}
            >
              <Box
                component="summary"
                sx={{ cursor: 'pointer', color: '#141414', fontWeight: 600 }}
              >
                Data coverage
              </Box>
              <Typography
                variant="body2"
                sx={{ mt: 1.5 }}
              >
                {data.historyCoverage}
              </Typography>
            </Box>
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

  private async load(): Promise<void> {
    if (!this.mounted) {
      return;
    }

    this.activePortfolioRequest?.abort();
    const controller = new AbortController();
    const requestGeneration = this.generation;
    this.activePortfolioRequest = controller;
    this.requests.add(controller);
    try {
      const next = await this.portfolioService.get(controller.signal);
      if (this.canUpdate(controller, requestGeneration)) {
        this.setState({ data: next, error: next.error });
      }
    } catch (cause) {
      if (this.canUpdate(controller, requestGeneration)) {
        this.setState({
          error: this.message(cause, 'Unable to load portfolio.'),
        });
      }
    } finally {
      this.requests.delete(controller);
      if (this.activePortfolioRequest === controller) {
        this.activePortfolioRequest = null;
      }
    }
  }

  private async refresh(): Promise<void> {
    if (!this.mounted || this.state.refreshing) {
      return;
    }

    this.setState({ refreshing: true });
    const controller = new AbortController();
    const requestGeneration = this.generation;
    this.requests.add(controller);
    try {
      await this.portfolioService.sync(controller.signal);
      if (!this.canUpdate(controller, requestGeneration)) {
        return;
      }

      await this.load();
    } catch (cause) {
      if (this.canUpdate(controller, requestGeneration)) {
        this.setState({
          error: this.message(cause, 'Unable to sync Bitkub data.'),
        });
      }
    } finally {
      this.requests.delete(controller);
      if (this.canUpdate(controller, requestGeneration)) {
        this.setState({ refreshing: false });
      }
    }
  }

  private updatePolling(): void {
    if (this.state.data?.syncing) {
      this.startPolling();
    } else {
      this.stopPolling();
    }
  }

  private startPolling(): void {
    if (this.pollTimer !== null) {
      return;
    }

    this.pollTimer = window.setInterval(() => {
      if (this.polling || this.activePortfolioRequest || !this.mounted) {
        return;
      }

      this.polling = true;
      void this.load().finally(() => {
        this.polling = false;
      });
    }, 2000);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      window.clearInterval(this.pollTimer);
      this.pollTimer = null;
    }

    this.polling = false;
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
        this.state.history[symbol] ||
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
    try {
      const response = await this.historyService.get(symbol, controller.signal);
      if (this.canUpdate(controller, requestGeneration)) {
        this.setState((current) => ({
          history: { ...current.history, [symbol]: response.trades },
        }));
      }
    } catch (cause) {
      if (this.canUpdate(controller, requestGeneration)) {
        this.setState((current) => ({
          historyErrors: {
            ...current.historyErrors,
            [symbol]: this.message(cause, 'Unable to load history.'),
          },
        }));
      }
    } finally {
      this.requests.delete(controller);
      if (this.historyRequests.get(symbol) === controller) {
        this.historyRequests.delete(symbol);
      }

      if (this.canUpdate(controller, requestGeneration)) {
        this.setState((current) => ({
          historyLoading: { ...current.historyLoading, [symbol]: false },
        }));
      }
    }
  }

  private canUpdate(
    controller: AbortController,
    requestGeneration: number,
  ): boolean {
    return (
      this.mounted &&
      !controller.signal.aborted &&
      requestGeneration === this.generation
    );
  }

  private message(cause: unknown, fallback: string): string {
    return cause instanceof Error ? cause.message : fallback;
  }
}
