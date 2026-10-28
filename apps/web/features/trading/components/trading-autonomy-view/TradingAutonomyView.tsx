import { Component, type ReactNode } from 'react';
import { Alert, Box, Chip, Stack, Typography } from '@mui/material';
import TradingFormatting from '../../presentation/TradingFormatting';
import type { TradingAutonomyViewProps } from './TradingAutonomyViewProps';

export default class TradingAutonomyView extends Component<TradingAutonomyViewProps> {
  public override render(): ReactNode {
    const overview = this.props.overview;
    const autonomy = overview.autonomy;
    if (!autonomy) {
      return (
        <Alert severity="info">
          Autonomous status is refreshing. No account or market values are shown
          until a verified snapshot is available.
        </Alert>
      );
    }

    const capital = autonomy.capital;
    const objective = overview.objective;
    const progress = objective
      ? `Portfolio target ${TradingFormatting.moneyNumber(objective.targetThb)}`
      : 'Target unavailable';

    return (
      <Box sx={{ minWidth: 0, overflowWrap: 'anywhere' }}>
        <Stack spacing={1}>
          <Stack
            direction="row"
            sx={{ justifyContent: 'space-between', gap: 1, flexWrap: 'wrap' }}
          >
            <Typography
              component="h3"
              sx={{ fontSize: 15, fontWeight: 700 }}
            >
              Autonomous trading
            </Typography>
            <Chip
              size="small"
              label={progress}
              color="primary"
            />
          </Stack>
          <Typography
            variant="caption"
            color="text.secondary"
          >
            {objective
              ? `Before ${TradingFormatting.time(objective.deadlineExclusive)} · Includes deposits`
              : 'This snapshot does not include an account target.'}
            {objective?.observedAt
              ? ` · Valued ${TradingFormatting.time(objective.observedAt)}`
              : ''}
          </Typography>
          {objective?.valuationStatus === 'unknown' && objective.reason && (
            <Typography
              variant="caption"
              color="text.secondary"
            >
              {objective.reason}
            </Typography>
          )}
          <Typography variant="body2">
            {autonomy.activity} · {autonomy.eligibleMarkets} of{' '}
            {autonomy.totalMarkets} markets eligible
          </Typography>
          {
            <Box
              component="dl"
              aria-label="Portfolio objective"
              sx={{
                m: 0,
                display: 'grid',
                gridTemplateColumns: {
                  xs: 'repeat(2, minmax(0, 1fr))',
                  sm: 'repeat(4, minmax(0, 1fr))',
                },
                gap: 1.5,
                p: 1.5,
                bgcolor: 'var(--surface-soft)',
                borderRadius: 'var(--radius-control)',
              }}
            >
              {[
                [
                  'Portfolio value',
                  objective?.portfolioValueThb === null ||
                  objective?.portfolioValueThb === undefined
                    ? 'Unknown'
                    : TradingFormatting.money(objective.portfolioValueThb),
                ],
                [
                  'Remaining',
                  objective?.remainingThb === null ||
                  objective?.remainingThb === undefined
                    ? 'Unknown'
                    : TradingFormatting.money(objective.remainingThb),
                ],
                [
                  'Deposits',
                  objective?.depositsThb === null ||
                  objective?.depositsThb === undefined
                    ? 'Unknown'
                    : TradingFormatting.money(objective.depositsThb),
                ],
                [
                  'Trading P&L',
                  objective?.tradingPnlThb === null ||
                  objective?.tradingPnlThb === undefined
                    ? 'Unknown'
                    : TradingFormatting.money(objective.tradingPnlThb),
                ],
              ].map(([label, value]) => (
                <Box key={label}>
                  <Typography
                    component="dt"
                    variant="caption"
                    color="text.secondary"
                  >
                    {label}
                  </Typography>
                  <Typography
                    component="dd"
                    sx={{
                      m: 0,
                      fontWeight: 700,
                      fontVariantNumeric: 'tabular-nums',
                    }}
                  >
                    {value}
                  </Typography>
                </Box>
              ))}
            </Box>
          }
          <Typography
            variant="body2"
            color="text.secondary"
          >
            Checked {autonomy.checkedMarkets} markets · Next check{' '}
            {autonomy.nextCheckAt === null
              ? 'Unavailable'
              : new Date(autonomy.nextCheckAt).toLocaleTimeString('en-GB', {
                  timeZone: 'Asia/Bangkok',
                }) + ' Bangkok'}
          </Typography>
          {autonomy.readiness && (
            <Typography variant="body2">
              Last scan: {String(autonomy.readiness.readyMarkets)} ready ·{' '}
              {String(autonomy.readiness.unavailableMarkets)} unavailable
            </Typography>
          )}
          {autonomy.funnel && (
            <Typography variant="body2">
              Today: {autonomy.funnel.signals} signals ·{' '}
              {autonomy.funnel.submittedOrders} orders ·{' '}
              {autonomy.funnel.filledOrders} fills · Net{' '}
              {TradingFormatting.money(autonomy.funnel.netThb)}
              {autonomy.funnel.coverage === 'retained'
                ? ' · Retained signal history'
                : ''}
            </Typography>
          )}
          {autonomy.readiness && (
            <Box component="details">
              <Box
                component="summary"
                sx={{
                  cursor: 'pointer',
                  minHeight: 44,
                  alignContent: 'center',
                  fontWeight: 600,
                }}
              >
                Readiness details
              </Box>
              {autonomy.readiness.frames.map((frame) => (
                <Typography
                  key={`${frame.symbol}-${String(frame.timeframeSeconds)}`}
                  variant="body2"
                  color="text.secondary"
                >
                  {frame.symbol} · {frame.timeframeSeconds / 60}m ·{' '}
                  {frame.status} · {frame.contiguousBars} consecutive bars ·{' '}
                  {frame.verifiedZeroVolumeBars} verified zero-volume bars · Gap
                  cause: {frame.gapCause}
                  {frame.reason ? ` · ${frame.reason}` : ''}
                  {' · '}
                  {new Date(frame.checkedAt).toLocaleTimeString('en-GB', {
                    timeZone: 'Asia/Bangkok',
                  })}{' '}
                  Bangkok · {frame.source}
                </Typography>
              ))}
              {autonomy.funnel?.blockedReasons.map((blocked) => (
                <Typography
                  key={blocked.reason}
                  variant="body2"
                  color="text.secondary"
                >
                  Blocked: {blocked.reason} ({blocked.count})
                </Typography>
              ))}
            </Box>
          )}
          {autonomy.evaluation && (
            <Box component="details">
              <Box
                component="summary"
                sx={{
                  cursor: 'pointer',
                  minHeight: 44,
                  alignContent: 'center',
                  fontWeight: 600,
                }}
              >
                Historical study ·{' '}
                {new Date(autonomy.evaluation.observedAt).toLocaleDateString(
                  'en-GB',
                  { timeZone: 'Asia/Bangkok' },
                )}
              </Box>
              <Typography
                variant="body2"
                color="text.secondary"
              >
                Hypothetical scenarios · Separate from account profit · Cash
                benchmark ฿0. Each row is independent; profits cannot be added
                together.
              </Typography>
              {autonomy.evaluation.warnings.map((warning) => (
                <Typography
                  key={warning}
                  variant="body2"
                  color="text.secondary"
                >
                  {warning}
                </Typography>
              ))}
              {autonomy.evaluation.metrics
                .filter((metric) => metric.segment === 'validation')
                .map((metric) => (
                  <Typography
                    key={`${metric.symbol}-${String(metric.timeframeSeconds)}-${metric.strategy}`}
                    variant="body2"
                    sx={{
                      py: 0.5,
                      fontVariantNumeric: 'tabular-nums',
                      overflowWrap: 'anywhere',
                    }}
                  >
                    {metric.symbol} {metric.timeframeSeconds / 60}m ·{' '}
                    {metric.strategy} · {metric.trades} closed ·{' '}
                    {metric.unknownFills} unknown · Net{' '}
                    {TradingFormatting.money(
                      metric.netThb === null ? null : String(metric.netThb),
                    )}{' '}
                    · Fees {TradingFormatting.money(String(metric.feesThb))} ·
                    Drawdown{' '}
                    {TradingFormatting.money(
                      metric.maxDrawdownThb === null
                        ? null
                        : String(metric.maxDrawdownThb),
                    )}
                    {metric.insufficientSamples
                      ? ' · Insufficient samples'
                      : ''}
                  </Typography>
                ))}
            </Box>
          )}
          {overview.mode === 'paper' ? (
            <Typography variant="body2">
              Simulated equity{' '}
              {TradingFormatting.money(overview.paper.equityThb)}
            </Typography>
          ) : (
            capital && (
              <Typography variant="body2">
                Equity {TradingFormatting.money(capital.equityThb)} · Reserved{' '}
                {TradingFormatting.money(capital.reservedThb)}
              </Typography>
            )
          )}
          {overview.mode !== 'paper' && capital?.blocker && (
            <Alert severity="info">{capital.blocker}</Alert>
          )}
          {autonomy.opportunityReview && (
            <Box component="details">
              <Box
                component="summary"
                sx={{
                  cursor: 'pointer',
                  minHeight: 44,
                  alignContent: 'center',
                  fontWeight: 600,
                }}
              >
                Opportunity review
              </Box>
              <Typography
                variant="body2"
                color="text.secondary"
              >
                {autonomy.opportunityReview.assumptions}
              </Typography>
              {autonomy.opportunityReview.days.length === 0 && (
                <Typography variant="body2">
                  Collecting benchmark observations.
                </Typography>
              )}
              {autonomy.opportunityReview.days.map((day) => (
                <Typography
                  key={day.date}
                  variant="body2"
                >
                  {day.date} · {day.closed} closed samples · {day.waiting}{' '}
                  pending · {day.unknown} unknown · Mean net return{' '}
                  {day.meanNetReturnFraction === null
                    ? 'Unknown'
                    : `${(day.meanNetReturnFraction * 100).toFixed(2)}%`}
                </Typography>
              ))}
            </Box>
          )}
          {autonomy.candidates.length > 0 && (
            <Box component="details">
              <Box
                component="summary"
                sx={{
                  cursor: 'pointer',
                  minHeight: 44,
                  alignContent: 'center',
                  fontWeight: 600,
                }}
              >
                Candidates · {autonomy.candidates.length}
              </Box>
              <Stack spacing={0.5}>
                {autonomy.candidates.map((candidate) => (
                  <Typography
                    key={`${candidate.symbol}-${candidate.at}`}
                    variant="body2"
                  >
                    {candidate.symbol} · {candidate.reason}
                  </Typography>
                ))}
              </Stack>
            </Box>
          )}
        </Stack>
      </Box>
    );
  }
}
