import { Divider, Paper, Stack } from '@mui/material';
import formatMoney from '../utils/formatMoney';
import SummaryMetric from './SummaryMetric';
import type { PortfolioSummaryProps } from './PortfolioSummaryProps';

export default function PortfolioSummary({ data }: PortfolioSummaryProps) {
  const missingAssetsCaption = data.updatedAt
    ? 'Price data incomplete'
    : 'Awaiting wallet data';
  const assetsCaption =
    data.summary.totalAssets == null
      ? missingAssetsCaption
      : 'Held crypto value';
  const missingCashCaption = data.updatedAt
    ? 'Balance unavailable'
    : 'Awaiting wallet data';
  const cashCaption =
    data.summary.availableThb == null
      ? missingCashCaption
      : 'Available to trade';

  return (
    <Paper
      component="section"
      aria-label="Portfolio summary"
      variant="outlined"
      sx={{
        borderColor: '#e0e0e0',
        borderRadius: '8px',
        boxShadow: 'none',
        p: { xs: 1.5, sm: 2 },
      }}
    >
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        divider={
          <Divider
            flexItem
            sx={{
              borderBottomWidth: { xs: 1, sm: 0 },
              borderRightWidth: { xs: 0, sm: 1 },
            }}
          />
        }
        spacing={0}
      >
        <SummaryMetric
          testId="total-assets"
          label="Total assets"
          value={
            data.summary.totalAssets == null
              ? '—'
              : formatMoney(data.summary.totalAssets)
          }
          caption={assetsCaption}
        />
        <SummaryMetric
          testId="available-thb"
          label="Available THB"
          value={
            data.summary.availableThb == null
              ? '—'
              : formatMoney(data.summary.availableThb)
          }
          caption={cashCaption}
        />
        <SummaryMetric
          testId="holding-count"
          label="Holding"
          value={
            data.summary.holdingCount == null
              ? '—'
              : String(data.summary.holdingCount)
          }
          caption="Positive crypto balances"
        />
      </Stack>
    </Paper>
  );
}
