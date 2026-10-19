import { Component, type ReactNode } from 'react';
import { Box, Stack, Typography } from '@mui/material';
import TradingFormatting from '../../presentation/TradingFormatting';
import TradingRulePresentation from '../../presentation/TradingRulePresentation';
import type { TradingBookViewProps } from './TradingBookViewProps';

export default class TradingBookView extends Component<TradingBookViewProps> {
  public override render(): ReactNode {
    const overview = this.props.overview;
    const adaptive = TradingRulePresentation.isAdaptive(overview.rules.version);
    const autonomy = overview.autonomy;
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
    let bookLabel = 'Live';
    if (adaptive) {
      bookLabel = overview.mode === 'paper' ? 'Simulated strategy' : 'Strategy';
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
      <Stack spacing={2}>
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
                minHeight: 36,
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
          aria-label={`${overview.mode === 'paper' ? 'Paper' : 'Live'} trading balances`}
          sx={{
            m: 0,
            display: 'grid',
            gridTemplateColumns: {
              xs: 'repeat(2, minmax(0, 1fr))',
              sm: 'repeat(4, minmax(0, 1fr))',
            },
            gap: 2,
            py: 2,
            borderBlock: '1px solid #e0e0e0',
          }}
        >
          {[
            [
              adaptive ? 'Available THB' : 'Cash',
              adaptive
                ? TradingFormatting.money(
                    autonomy?.capital?.availableThb ?? null,
                  )
                : TradingFormatting.money(book.cashThb),
            ],
            [
              adaptive ? 'Exposure incl. orders' : 'In positions',
              adaptive
                ? TradingFormatting.money(
                    autonomy?.capital?.exposureThb ?? null,
                  )
                : TradingFormatting.money(String(exposure)),
            ],
            [
              adaptive ? 'Net today' : 'Realized net P&L',
              TradingFormatting.money(
                adaptive
                  ? (autonomy?.realizedNetTodayThb ?? null)
                  : book.realizedNetThb,
              ),
            ],
            ['Open orders', String(openOrders.length)],
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
            Positions &amp; orders · {book.positions.length} positions ·{' '}
            {openOrders.length} open
          </Box>
          <Stack
            spacing={1.5}
            sx={{ py: 1 }}
          >
            <Typography variant="body2">
              {bookLabel} book · Equity{' '}
              {TradingFormatting.money(
                adaptive
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
                No positions or open orders in this book.
              </Typography>
            )}
            {book.positions.map((position) => (
              <Box
                key={position.symbol}
                sx={{ display: 'grid', gap: 0.25 }}
              >
                <Typography variant="body2">
                  {position.symbol}: {position.quantity} · Cost{' '}
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
            {openOrders.map((order) => (
              <Typography
                key={order.id}
                variant="body2"
                sx={{ overflowWrap: 'anywhere' }}
              >
                {order.side.toUpperCase()} {order.symbol} · Amount{' '}
                {order.amount} · Limit {order.limitPrice} THB · Remaining{' '}
                {order.remaining} · {order.status} · {order.reason}
              </Typography>
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
}
