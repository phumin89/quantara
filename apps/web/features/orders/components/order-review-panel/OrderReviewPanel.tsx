'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import { Alert, Box, Button, Paper, Stack, Typography } from '@mui/material';
import CheckCircleOutlineOutlinedIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import BlockOutlinedIcon from '@mui/icons-material/BlockOutlined';
import { OrdersServiceContext } from '../../context/OrdersServiceContext';
import type { OrderView } from '../../contracts/OrderView';
import { OrderSide } from '../../contracts/OrderSide';
import { OrderStatus } from '../../contracts/OrderStatus';
import type { OrderReviewPanelState } from './OrderReviewPanelState';

export default class OrderReviewPanel extends Component<
  object,
  OrderReviewPanelState
> {
  public static override readonly contextType = OrdersServiceContext.value;

  public override context: ContextType<typeof OrdersServiceContext.value> =
    null;

  private mounted = false;

  private timer: number | null = null;

  private request = new AbortController();

  private generation = 0;

  private actionInFlight = false;

  public override state: OrderReviewPanelState = {
    proposals: [],
    loading: true,
    error: null,
    busyId: null,
    seconds: 0,
  };

  public override componentDidMount(): void {
    this.mounted = true;
    this.request = new AbortController();
    this.startTimer();
    void this.load();
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.request.abort();
    this.clearTimer();
  }

  public override render(): ReactNode {
    return (
      <Paper
        variant="outlined"
        sx={{ p: { xs: 1.5, sm: 2 }, borderRadius: '8px' }}
      >
        <Stack spacing={1.25}>
          <Typography
            component="h2"
            sx={{ fontSize: 16, fontWeight: 700 }}
          >
            Trade proposals
          </Typography>
          {this.state.error && (
            <Alert severity="error">{this.state.error}</Alert>
          )}
          {this.state.loading ? (
            <Typography
              variant="body2"
              color="text.secondary"
            >
              Loading order proposals…
            </Typography>
          ) : (
            this.renderProposals()
          )}
        </Stack>
      </Paper>
    );
  }

  private renderProposals(): ReactNode {
    if (!this.state.proposals.length) {
      return (
        <Typography
          variant="body2"
          color="text.secondary"
        >
          No order proposals.
        </Typography>
      );
    }

    return (
      <Stack spacing={1}>
        {this.state.proposals.map((proposal) => this.renderProposal(proposal))}
      </Stack>
    );
  }

  private renderProposal(proposal: OrderView): ReactNode {
    const remaining = this.remaining(proposal);
    const isPending = proposal.status === OrderStatus.Pending;

    return (
      <Box
        key={proposal.id}
        sx={{ borderBottom: '1px solid #e0e0e0', pb: 1.25 }}
      >
        <Stack spacing={0.75}>
          <Typography sx={{ fontWeight: 700 }}>
            {proposal.side === OrderSide.Buy
              ? `Buy ${proposal.symbol} for ${this.decimal(proposal.amount)} THB`
              : `Sell ${this.decimal(proposal.amount)} ${proposal.symbol}`}{' '}
            at limit {this.decimal(proposal.limitPrice)} THB/{proposal.symbol}
          </Typography>
          <Typography
            variant="caption"
            color="text.secondary"
          >
            {proposal.side === OrderSide.Sell &&
              `Approx. proceeds ${this.decimal(proposal.notionalThb)} THB · `}
            Estimated fee {proposal.estimatedFeeThb} THB · {proposal.reason}
          </Typography>
          <Typography
            variant="caption"
            color="text.secondary"
          >
            {this.statusLabel(proposal, remaining)}
          </Typography>
          {proposal.message && (
            <Alert
              severity={
                proposal.status === OrderStatus.Unknown ? 'warning' : 'info'
              }
            >
              {proposal.message}
            </Alert>
          )}
          {isPending && (
            <Stack
              direction="row"
              spacing={1}
            >
              <Button
                variant="contained"
                disableElevation
                color="success"
                size="small"
                startIcon={<CheckCircleOutlineOutlinedIcon />}
                disabled={remaining <= 0 || this.state.busyId !== null}
                onClick={() => void this.approve(proposal.id)}
                sx={{ minHeight: 44, textTransform: 'none' }}
              >
                Approve
              </Button>
              <Button
                variant="outlined"
                color="inherit"
                size="small"
                startIcon={<BlockOutlinedIcon />}
                disabled={this.state.busyId !== null}
                onClick={() => void this.reject(proposal.id)}
                sx={{ minHeight: 44, textTransform: 'none' }}
              >
                Reject
              </Button>
            </Stack>
          )}
        </Stack>
      </Box>
    );
  }

  private async load(): Promise<void> {
    const generation = ++this.generation;
    try {
      const response = await this.service().list(this.request.signal);
      if (this.mounted && generation === this.generation) {
        this.setState({ proposals: response.proposals, loading: false }, () => {
          this.startTimer();
        });
      }
    } catch {
      if (
        this.mounted &&
        generation === this.generation &&
        !this.request.signal.aborted
      ) {
        this.setState({
          loading: false,
          error: 'Unable to load order proposals.',
        });
      }
    }
  }

  private startTimer(): void {
    this.clearTimer();
    this.timer = window.setInterval(() => {
      if (this.mounted) {
        this.setState({ seconds: this.state.seconds + 1 });
      }

      if (
        this.mounted &&
        this.state.seconds % 5 === 0 &&
        !this.actionInFlight
      ) {
        void this.load();
      }
    }, 1000);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      window.clearInterval(this.timer);
      this.timer = null;
    }
  }

  private async approve(id: string): Promise<void> {
    await this.mutate(id, 'approve');
  }

  private async reject(id: string): Promise<void> {
    await this.mutate(id, 'reject');
  }

  private async mutate(
    id: string,
    action: 'approve' | 'reject',
  ): Promise<void> {
    const proposal = this.state.proposals.find((item) => item.id === id);
    if (
      this.actionInFlight ||
      this.state.busyId !== null ||
      !proposal ||
      proposal.status !== OrderStatus.Pending ||
      (action === 'approve' && this.remaining(proposal) <= 0)
    ) {
      return;
    }

    this.actionInFlight = true;
    this.generation += 1;
    this.setState({ busyId: id, error: null });
    try {
      const result = await (action === 'approve'
        ? this.service().approve(id)
        : this.service().reject(id));
      if (this.mounted) {
        this.setState({
          proposals: this.state.proposals.map((item) =>
            item.id === id ? result : item,
          ),
          busyId: null,
        });
      }
    } catch (cause) {
      if (this.mounted) {
        this.setState({
          busyId: null,
          error:
            cause instanceof Error
              ? cause.message
              : 'Order result is unknown. Check Bitkub and reload; never resubmit automatically.',
        });
        await this.load();
      }
    } finally {
      this.actionInFlight = false;
    }
  }

  private service() {
    return OrdersServiceContext.require(this.context);
  }

  private statusLabel(proposal: OrderView, remaining: number): string {
    if (proposal.status === OrderStatus.Pending) {
      return remaining > 0
        ? `Expires in ${this.formatSeconds(remaining)}`
        : 'Expired';
    }

    return proposal.status;
  }

  private remaining(proposal: OrderView): number {
    return Math.max(
      0,
      Math.ceil((Date.parse(proposal.expiresAt) - Date.now()) / 1000),
    );
  }

  private formatSeconds(seconds: number): string {
    return `${String(Math.floor(seconds / 60))}:${String(seconds % 60).padStart(2, '0')}`;
  }

  private decimal(value: string): string {
    const parts = value.split('.');
    const integer = (parts[0] ?? '').replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    const fraction = parts[1];

    return fraction === undefined ? integer : `${integer}.${fraction}`;
  }
}
