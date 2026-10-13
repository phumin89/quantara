'use client';

import {
  Component,
  type ChangeEvent,
  type ContextType,
  type ReactNode,
} from 'react';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Divider,
  FormControlLabel,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import AddTaskOutlinedIcon from '@mui/icons-material/AddTaskOutlined';
import CheckCircleOutlineOutlinedIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import BlockOutlinedIcon from '@mui/icons-material/BlockOutlined';
import { OrdersServiceContext } from '../../context/OrdersServiceContext';
import type { OrderView } from '../../contracts/OrderView';
import { OrderSide } from '../../contracts/OrderSide';
import { OrderStatus } from '../../contracts/OrderStatus';
import type { OrderDraft } from '../../contracts/OrderDraft';
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
    symbol: '',
    side: OrderSide.Buy,
    amount: '',
    limitPrice: '',
    reason: '',
    seconds: 0,
    reviewedIds: [],
  };

  public override componentDidMount(): void {
    this.mounted = true;
    this.request = new AbortController();
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
        sx={{ p: { xs: 2, sm: 2.5 }, borderRadius: '8px' }}
      >
        <Stack spacing={2}>
          <Stack
            direction="row"
            spacing={1}
            sx={{ alignItems: 'center' }}
          >
            <AddTaskOutlinedIcon fontSize="small" />
            <Typography
              component="h2"
              sx={{ fontSize: 16, fontWeight: 700, flex: 1 }}
            >
              Order review
            </Typography>
            <Chip
              size="small"
              label="Limit spot only"
              sx={{ borderRadius: '4px', color: '#1e40af', bgcolor: '#dbeafe' }}
            />
          </Stack>
          <Button
            onClick={() => void this.load()}
            disabled={this.actionInFlight}
            sx={{ alignSelf: 'flex-start', minHeight: 44 }}
          >
            Refresh orders
          </Button>
          <Typography
            variant="body2"
            color="text.secondary"
          >
            Orders require an explicit approval. Waiting or expiry never
            approves an order automatically. This is the current order queue,
            independent of the selected report date.
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
          <Divider />
          <Typography sx={{ fontSize: 14, fontWeight: 700 }}>
            Manual proposal
          </Typography>
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: {
                xs: '1fr',
                sm: 'repeat(2, minmax(0, 1fr))',
                xl: 'repeat(4, minmax(0, 1fr))',
              },
              gap: 1.5,
            }}
          >
            <TextField
              label="Symbol"
              value={this.state.symbol}
              onChange={this.change('symbol')}
              placeholder="BTC"
              size="small"
              slotProps={{ htmlInput: { maxLength: 30 } }}
            />
            <TextField
              select
              label="Side"
              value={this.state.side}
              onChange={this.change('side')}
              size="small"
              sx={{ minWidth: 110 }}
            >
              <MenuItem value="buy">Buy</MenuItem>
              <MenuItem value="sell">Sell</MenuItem>
            </TextField>
            <TextField
              label={
                this.state.side === OrderSide.Buy
                  ? 'THB spend'
                  : 'Coin quantity'
              }
              value={this.state.amount}
              onChange={this.change('amount')}
              size="small"
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
            />
            <TextField
              label="Limit price (THB)"
              value={this.state.limitPrice}
              onChange={this.change('limitPrice')}
              size="small"
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
            />
          </Box>
          <TextField
            label="Reason"
            value={this.state.reason}
            onChange={this.change('reason')}
            multiline
            minRows={2}
            slotProps={{ htmlInput: { maxLength: 500 } }}
          />
          <Button
            variant="outlined"
            onClick={() => void this.create()}
            disabled={!this.canCreate()}
            sx={{
              alignSelf: 'flex-start',
              minHeight: 44,
              textTransform: 'none',
            }}
          >
            Create proposal
          </Button>
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
      <Stack spacing={1.5}>
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
        sx={{ border: '1px solid #e0e0e0', borderRadius: '6px', p: 1.5 }}
      >
        <Stack spacing={1.25}>
          <Stack
            direction={{ xs: 'column', sm: 'row' }}
            spacing={1}
            sx={{ alignItems: { sm: 'center' } }}
          >
            <Typography sx={{ fontWeight: 700, flex: 1 }}>
              {proposal.side === OrderSide.Buy ? 'Buy' : 'Sell'}{' '}
              {proposal.symbol}
            </Typography>
            <Chip
              size="small"
              label={this.statusLabel(proposal, remaining)}
              sx={{
                borderRadius: '4px',
                color: isPending && remaining > 0 ? '#92400e' : '#525252',
                bgcolor: isPending && remaining > 0 ? '#fef3c7' : '#f3f3f3',
              }}
            />
          </Stack>
          <Typography variant="body2">
            {this.decimal(proposal.amount)}{' '}
            {proposal.side === OrderSide.Buy ? 'THB spend' : proposal.symbol} ·
            Limit {this.decimal(proposal.limitPrice)} THB/{proposal.symbol} ·
            Reference {this.decimal(proposal.referencePrice)} THB/
            {proposal.symbol}
          </Typography>
          <Typography
            variant="caption"
            color="text.secondary"
          >
            {proposal.side === OrderSide.Buy
              ? 'Maximum buy spend'
              : 'Gross sell proceeds'}
            : {this.decimal(proposal.notionalThb)} THB · Estimated fee{' '}
            {proposal.estimatedFeeThb} THB · {proposal.reason}
          </Typography>
          <Typography
            variant="caption"
            color="text.secondary"
          >
            THB fees are deducted from buy spend or sell proceeds; estimates
            exclude fee credits. A price move above 1% requires a new proposal.
            Approval never changes your limit price.
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
          <Stack
            direction={{ xs: 'column', lg: 'row' }}
            spacing={1}
          >
            {isPending && (
              <FormControlLabel
                control={
                  <Checkbox
                    checked={this.state.reviewedIds.includes(proposal.id)}
                    disabled={remaining <= 0 || this.state.busyId !== null}
                    onChange={(event) => {
                      this.review(proposal.id, event.target.checked);
                    }}
                  />
                }
                label="I reviewed this exact order"
              />
            )}
            <Stack
              direction={{ xs: 'column', sm: 'row' }}
              spacing={1}
              sx={{ width: { xs: '100%', sm: 'auto' } }}
            >
              {isPending && (
                <Button
                  variant="contained"
                  color="success"
                  size="small"
                  startIcon={<CheckCircleOutlineOutlinedIcon />}
                  disabled={
                    remaining <= 0 ||
                    this.state.busyId !== null ||
                    !this.state.reviewedIds.includes(proposal.id)
                  }
                  onClick={() => void this.approve(proposal.id)}
                  sx={{ minHeight: 44, textTransform: 'none' }}
                >
                  Approve &amp; submit
                </Button>
              )}
              {isPending && (
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
              )}
            </Stack>
          </Stack>
          <Typography
            variant="caption"
            color="text.secondary"
          >
            Approval expiry does not cancel an accepted Bitkub limit order.
            Submitted is not filled. Manage open orders in Bitkub.
          </Typography>
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
        this.state.proposals.some(
          (proposal) =>
            proposal.status === OrderStatus.Pending ||
            proposal.status === OrderStatus.Submitting,
        ) &&
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
      (action === 'approve' &&
        (this.remaining(proposal) <= 0 || !this.state.reviewedIds.includes(id)))
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
          reviewedIds: this.state.reviewedIds.filter(
            (reviewed) => reviewed !== id,
          ),
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

  private create(): Promise<void> {
    if (!this.canCreate()) {
      return Promise.resolve();
    }

    this.actionInFlight = true;
    this.generation += 1;
    const { symbol, side, amount, limitPrice, reason } = this.state;
    this.setState({ busyId: 'new', error: null });

    const draft: OrderDraft = {
      symbol: symbol.toUpperCase(),
      side,
      amount,
      limitPrice,
      reason,
    };

    return this.service()
      .create(draft)
      .then((proposal) => {
        if (this.mounted) {
          this.setState({
            proposals: [proposal, ...this.state.proposals],
            symbol: '',
            amount: '',
            limitPrice: '',
            reason: '',
            busyId: null,
          });
        }
      })
      .catch((cause: unknown) => {
        if (this.mounted) {
          this.setState({
            busyId: null,
            error:
              cause instanceof Error
                ? cause.message
                : 'Unable to create the order proposal.',
          });
        }
      })
      .finally(() => {
        this.actionInFlight = false;
      });
  }

  private canCreate(): boolean {
    return (
      !this.actionInFlight &&
      this.state.busyId === null &&
      Number(this.state.amount) > 0 &&
      Number(this.state.limitPrice) > 0 &&
      /^[a-z0-9]{1,30}$/i.test(this.state.symbol) &&
      /^(?:0|[1-9]\d{0,17})(?:\.\d{1,18})?$/.test(this.state.amount) &&
      /^(?:0|[1-9]\d{0,17})(?:\.\d{1,18})?$/.test(this.state.limitPrice) &&
      this.state.reason.trim().length > 0
    );
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

  private review(id: string, reviewed: boolean): void {
    this.setState({
      reviewedIds: reviewed
        ? [...this.state.reviewedIds, id]
        : this.state.reviewedIds.filter((item) => item !== id),
    });
  }

  private change(
    field: 'symbol' | 'side' | 'amount' | 'limitPrice' | 'reason',
  ) {
    return (event: ChangeEvent<HTMLInputElement>) => {
      this.setState({ [field]: event.target.value } as Pick<
        OrderReviewPanelState,
        typeof field
      >);
    };
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
