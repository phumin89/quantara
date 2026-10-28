import { Component, type ReactNode } from 'react';
import { Box, Stack, Typography } from '@mui/material';
import TradingFormatting from '../../presentation/TradingFormatting';
import TradingRulePresentation from '../../presentation/TradingRulePresentation';
import type { TradingOverview } from '../../contracts/TradingOverview';
import type { TradingBookViewProps } from './TradingBookViewProps';

export default class TradingBookView extends Component<TradingBookViewProps> {
  public override render(): ReactNode {
    const overview = this.props.overview;
    const adaptive = TradingRulePresentation.isAdaptive(overview.rules.version);
    const paper = overview.mode === 'paper';
    const observedCapital = adaptive && !paper;
    const autonomy = overview.autonomy;
    const book = paper ? overview.paper : overview.live;
    const liveReviewNeeded = overview.live.orders.some((order) =>
      this.needsReview(order),
    );
    const openOrders = book.orders.filter(
      (order) =>
        !['filled', 'cancelled', 'rejected', 'expired'].includes(
          order.status.toLowerCase(),
        ) || this.needsReview(order),
    );
    const recentOrders = book.orders
      .filter(
        (order) =>
          ['cancelled', 'rejected', 'expired'].includes(order.status) &&
          !this.needsReview(order),
      )
      .sort(
        (left, right) =>
          Date.parse(right.createdAt) - Date.parse(left.createdAt),
      )
      .slice(0, 10);
    const exposure = TradingFormatting.sum(
      book.positions.map((position) => position.costThb),
    );
    let cashLabel = adaptive ? 'Observed available THB' : 'Ledger cash';
    let costLabel = adaptive ? 'Observed exposure incl. orders' : 'Ledger cost';
    let netLabel = adaptive ? 'Net today' : 'Realized net P&L';
    if (paper) {
      cashLabel = 'Simulated cash';
      costLabel = 'Simulated cost';
      netLabel = adaptive
        ? 'Simulated net today'
        : 'Simulated realized net P&L';
    }

    let bookLabel = 'Strategy ledger';
    if (adaptive) {
      bookLabel =
        overview.mode === 'paper' ? 'Simulated strategy' : 'Strategy ledger';
    } else if (overview.mode === 'paper') {
      bookLabel = 'Paper';
    }

    let studyStatus = 'Insufficient evidence';
    if (overview.exitStudy?.status === 'readyForReview') {
      studyStatus = 'Ready for review';
    }

    if (overview.exitStudy?.status === 'collecting') {
      studyStatus = 'Collecting evidence';
    }

    if (
      overview.exitStudy &&
      overview.exitStudy.liveSamples + overview.exitStudy.paperSamples === 0
    ) {
      studyStatus = 'Waiting for prospective entries';
    }

    return (
      <Stack
        spacing={2}
        sx={{ minWidth: 0, overflowWrap: 'anywhere' }}
      >
        {overview.mode === 'paper' &&
          liveReviewNeeded &&
          this.renderOrders(
            overview.live.orders.filter(
              (order) =>
                !['filled', 'cancelled', 'rejected', 'expired'].includes(
                  order.status,
                ) || this.needsReview(order),
            ),
            'Live orders needing review',
          )}
        {this.renderOrders(
          openOrders,
          overview.mode === 'paper'
            ? 'Simulated active orders'
            : 'Active orders',
        )}
        {recentOrders.length > 0 && (
          <Box
            component="details"
            sx={{
              '& > summary': {
                cursor: 'pointer',
                minHeight: 44,
                alignContent: 'center',
                fontWeight: 600,
              },
            }}
          >
            <Box component="summary">
              Recent order history ({recentOrders.length})
            </Box>
            {this.renderOrders(recentOrders, 'Recent completed order history')}
          </Box>
        )}
        {overview.mode !== 'paper' && (
          <Typography
            variant="body2"
            color="text.secondary"
          >
            Accounting lots can differ from wallet holdings after external
            trades. Use the Bitkub wallet snapshot for holdings; broker order
            confirmation determines whether an intent reached the exchange.
          </Typography>
        )}
        {overview.notifications && (
          <Typography
            variant="body2"
            color="text.secondary"
          >
            Notifications: {overview.notifications.pending} pending
            {overview.notifications.oldestPendingAt
              ? ` · oldest ${TradingFormatting.age(overview.notifications.oldestPendingAt)}`
              : ''}
            {overview.notifications.unknown +
              (overview.notifications.blocked ?? 0) >
            0
              ? ` · Needs review ${String(overview.notifications.unknown + (overview.notifications.blocked ?? 0))}`
              : ''}
            {overview.notifications.dashboardAvailable && (
              <>
                {' · '}
                <a
                  href="/api/trading/jobs"
                  target="_blank"
                  rel="noreferrer"
                >
                  Job history
                </a>
              </>
            )}
          </Typography>
        )}
        {overview.exitStudy && (
          <Box
            component="details"
            sx={{
              '& > summary': {
                cursor: 'pointer',
                minHeight: 44,
                alignContent: 'center',
                fontSize: 13,
                fontWeight: 600,
              },
            }}
          >
            <Box component="summary">Adaptive exit study · Research only</Box>
            <Typography
              variant="caption"
              color="text.secondary"
            >
              {overview.exitStudy.pairedClosed} /{' '}
              {overview.exitStudy.minimumClosed} live pairs · Required{' '}
              {overview.exitStudy.minimumDays} days ·{' '}
              {overview.exitStudy.liveSamples + overview.exitStudy.paperSamples}{' '}
              prospective samples
              {' · '}
              {studyStatus}
              {
                ' · Values are hypothetical matched comparisons; no account-profit substitution or live-ready claim.'
              }
            </Typography>
          </Box>
        )}
        <Box
          component="dl"
          aria-label={`${overview.mode === 'paper' ? 'Paper' : 'Strategy ledger'} balances`}
          sx={{
            m: 0,
            display: 'grid',
            gridTemplateColumns: {
              xs: 'repeat(2, minmax(0, 1fr))',
              sm: 'repeat(4, minmax(0, 1fr))',
            },
            gap: 2,
            py: 2,
            borderBlock: '1px solid',
            borderColor: 'divider',
          }}
        >
          {[
            [
              cashLabel,
              observedCapital
                ? TradingFormatting.money(
                    autonomy?.capital?.availableThb ?? null,
                  )
                : TradingFormatting.money(book.cashThb),
            ],
            [
              costLabel,
              observedCapital
                ? TradingFormatting.money(
                    autonomy?.capital?.exposureThb ?? null,
                  )
                : TradingFormatting.money(exposure),
            ],
            [
              netLabel,
              TradingFormatting.money(
                adaptive
                  ? (autonomy?.realizedNetTodayThb ?? null)
                  : book.realizedNetThb,
              ),
            ],
            ['Tracked intents', String(openOrders.length)],
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
        {!adaptive && (
          <Typography
            variant="body2"
            color="text.secondary"
          >
            {overview.rules.symbols.join(', ')} ·{' '}
            {TradingFormatting.money(String(overview.rules.allocatedThb))}{' '}
            allocated ·{' '}
            {TradingFormatting.money(String(overview.rules.perOrderThb))} per
            order
          </Typography>
        )}
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
            Ledger details · {book.positions.length}{' '}
            {book.positions.length === 1 ? 'lot' : 'lots'}
          </Box>
          <Stack
            spacing={1.5}
            sx={{ py: 1 }}
          >
            <Typography variant="body2">
              {bookLabel} book · Equity{' '}
              {TradingFormatting.money(
                observedCapital
                  ? (autonomy?.capital?.equityThb ?? null)
                  : book.equityThb,
              )}{' '}
              · Gross P&amp;L {TradingFormatting.money(book.realizedGrossThb)} ·
              Fees {TradingFormatting.money(book.feesThb)} · Unrealized{' '}
              {TradingFormatting.money(book.unrealizedThb)}
            </Typography>
            {book.positions.length === 0 && openOrders.length === 0 && (
              <Typography
                variant="body2"
                color="text.secondary"
              >
                No ledger lots or active intents in this book.
              </Typography>
            )}
            {book.positions.map((position) => (
              <Box
                key={position.symbol}
                sx={{ display: 'grid', gap: 0.25 }}
              >
                <Typography variant="body2">
                  {position.symbol}: Ledger quantity {position.quantity} · Cost{' '}
                  {TradingFormatting.money(position.costThb)} · Opened{' '}
                  {TradingFormatting.time(position.openedAt)}
                </Typography>
                {position.exitPlan && (
                  <Typography
                    variant="caption"
                    color="text.secondary"
                  >
                    Exit {position.exitPlan.regime} · Protective stop{' '}
                    {position.exitPlan.effectiveStop} · Next review{' '}
                    {TradingFormatting.time(position.exitPlan.nextReviewAt)} ·
                    Maximum hold until{' '}
                    {TradingFormatting.time(position.exitPlan.hardExitAt)} ·{' '}
                    {position.exitPlan.lastReason}
                  </Typography>
                )}
              </Box>
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
                {TradingFormatting.time(
                  overview.mandate.revokedAt ?? overview.mandate.expiresAt,
                )}
              </Typography>
            )}
          </Stack>
        </Box>
      </Stack>
    );
  }

  private needsReview(
    order: TradingOverview['live']['orders'][number],
  ): boolean {
    return (
      ['submitting', 'unknown'].includes(order.status) ||
      ['claimed', 'unknown'].includes(order.cancellationState ?? '')
    );
  }

  private renderOrders(
    orders: TradingOverview['live']['orders'],
    label: string,
  ): ReactNode {
    return (
      <Box
        component="section"
        aria-label={label}
        sx={{ display: 'grid', gap: 1 }}
      >
        <Typography
          component="h3"
          sx={{ fontSize: 14, fontWeight: 700 }}
        >
          {label} ({orders.length})
        </Typography>
        {orders.length === 0 ? (
          <Typography
            variant="body2"
            color="text.secondary"
          >
            No active orders.
          </Typography>
        ) : (
          <Stack
            component="ul"
            spacing={0}
            sx={{ m: 0, p: 0 }}
          >
            {orders.map((order) => (
              <Box
                key={order.id}
                component="li"
                sx={{
                  listStyle: 'none',
                  borderBottom: '1px solid',
                  borderColor: 'divider',
                  py: 1,
                }}
              >
                <Box
                  sx={{ display: 'grid', gap: 0.5, overflowWrap: 'anywhere' }}
                >
                  <Box sx={{ display: 'grid', gap: 0.5 }}>
                    <Typography
                      component="span"
                      sx={{ fontWeight: 600 }}
                    >
                      {order.side.toUpperCase()} {order.symbol} ·{' '}
                      {this.orderStatus(order)}
                    </Typography>
                    <Typography
                      component="span"
                      variant="body2"
                      color="text.secondary"
                      sx={{ fontVariantNumeric: 'tabular-nums' }}
                    >
                      Amount {this.orderAmount(order)} · Remaining{' '}
                      {this.orderAmount(order, true)} · Limit {order.limitPrice}{' '}
                      THB
                    </Typography>
                    {order.originalAmount &&
                      order.originalAmount !== order.amount && (
                        <Typography
                          component="span"
                          variant="caption"
                          color="text.secondary"
                        >
                          Adjusted from {order.originalAmount}
                        </Typography>
                      )}
                    <Typography
                      component="span"
                      variant="caption"
                      color="text.secondary"
                    >
                      Created {TradingFormatting.time(order.createdAt)}
                    </Typography>
                    <Typography
                      component="span"
                      variant="caption"
                      color="text.secondary"
                    >
                      {order.lastInspectedAt
                        ? `Last check ${TradingFormatting.time(order.lastInspectedAt)}`
                        : 'No check time recorded'}
                      {order.lastInspectionError
                        ? ` · ${order.lastInspectionError}`
                        : ''}
                    </Typography>
                    {order.lastExecutionMessage &&
                      !['cancelled', 'rejected', 'expired'].includes(
                        order.status,
                      ) && (
                        <Typography
                          component="span"
                          variant="body2"
                          sx={{ fontWeight: 600 }}
                        >
                          Execution update
                          {order.lastExecutionAt
                            ? ` · ${TradingFormatting.time(order.lastExecutionAt)}`
                            : ''}
                          : {order.lastExecutionMessage}
                        </Typography>
                      )}
                  </Box>
                  <Box
                    component="details"
                    sx={{
                      '& > summary': {
                        cursor: 'pointer',
                        minHeight: 44,
                        alignContent: 'center',
                        fontWeight: 600,
                        '&:focus-visible': {
                          outline: '2px solid',
                          outlineOffset: 2,
                        },
                      },
                    }}
                  >
                    <Box component="summary">Order details</Box>
                    <Typography
                      component="p"
                      variant="caption"
                      color="text.secondary"
                      sx={{ overflowWrap: 'anywhere' }}
                    >
                      Request ID {order.id} · Broker ID{' '}
                      {order.brokerOrderId ?? 'Unavailable'}
                    </Typography>
                    <Typography
                      component="p"
                      variant="caption"
                      color="text.secondary"
                      sx={{ overflowWrap: 'anywhere' }}
                    >
                      Strategy reason: {order.reason}
                    </Typography>
                    {order.lastExecutionMessage &&
                      ['cancelled', 'rejected', 'expired'].includes(
                        order.status,
                      ) && (
                        <Typography
                          component="p"
                          variant="body2"
                          sx={{ fontWeight: 600 }}
                        >
                          {order.status === 'cancelled' &&
                          !order.brokerOrderId &&
                          !order.cancellationState &&
                          order.lastExecutionMessage.startsWith(
                            'Not submitted:',
                          )
                            ? 'Canceled before send'
                            : 'Execution update'}
                          {order.lastExecutionAt
                            ? ` · ${TradingFormatting.time(order.lastExecutionAt)}`
                            : ''}
                          : {order.lastExecutionMessage}
                        </Typography>
                      )}
                  </Box>
                </Box>
              </Box>
            ))}
          </Stack>
        )}
      </Box>
    );
  }

  private orderStatus(
    order: TradingOverview['live']['orders'][number],
  ): string {
    if (
      order.cancellationState === 'claimed' ||
      order.cancellationState === 'unknown'
    ) {
      return 'Cancellation unconfirmed';
    }

    if (order.status === 'unknown') {
      return 'Outcome unknown';
    }

    if (order.status === 'submitting') {
      return 'Awaiting confirmation';
    }

    return order.status.charAt(0).toUpperCase() + order.status.slice(1);
  }

  private orderAmount(
    order: TradingOverview['live']['orders'][number],
    remaining = false,
  ): string {
    const value = remaining ? order.remaining : order.amount;

    return `${value} ${order.side === 'buy' ? 'THB' : order.symbol}`;
  }
}
