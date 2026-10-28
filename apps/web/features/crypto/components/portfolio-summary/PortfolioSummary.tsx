import { PureComponent } from 'react';
import { Divider, Paper, Stack } from '@mui/material';
import FormatMoney from '../../utils/FormatMoney';
import SummaryMetric from '../summary-metric/SummaryMetric';
import type { PortfolioSummaryProps } from './PortfolioSummaryProps';

export default class PortfolioSummary extends PureComponent<PortfolioSummaryProps> {
  public override render(): React.ReactNode {
    const { data } = this.props;
    const sourceAt = data.updatedAt ? Date.parse(data.updatedAt) : NaN;
    const hasWalletSnapshot =
      this.props.holdingsKnown ??
      (Number.isFinite(sourceAt) && sourceAt - Date.now() <= 30000);
    const missingAssetsCaption = data.updatedAt
      ? 'Price data incomplete'
      : 'Awaiting wallet data';
    const assetsCaption =
      data.summary.totalAssets == null
        ? missingAssetsCaption
        : 'Held assets · excludes THB';
    const missingCashCaption = data.updatedAt
      ? 'Balance unavailable'
      : 'Awaiting wallet data';
    const cashCaption =
      data.summary.availableThb == null
        ? missingCashCaption
        : 'Unreserved broker balance';

    return (
      <Paper
        component="section"
        aria-label="Portfolio summary"
        variant="outlined"
        sx={{
          borderColor: 'var(--border-panel)',
          borderRadius: 'var(--radius-panel)',
          bgcolor: 'var(--surface-panel)',
          boxShadow: 'var(--shadow-panel)',
          p: 'var(--panel-padding)',
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
          sx={{
            alignItems: 'stretch',
            '& > .MuiBox-root:first-of-type': {
              bgcolor: 'var(--color-mint)',
              borderRadius: 'var(--radius-control)',
              px: 2,
            },
          }}
        >
          <SummaryMetric
            testId="total-assets"
            label="Crypto holdings value"
            value={
              !hasWalletSnapshot || data.summary.totalAssets == null
                ? '—'
                : FormatMoney.format(data.summary.totalAssets)
            }
            caption={assetsCaption}
          />
          <SummaryMetric
            testId="available-thb"
            label="Available THB"
            value={
              !hasWalletSnapshot || data.summary.availableThb == null
                ? '—'
                : FormatMoney.format(data.summary.availableThb)
            }
            caption={cashCaption}
          />
          <SummaryMetric
            testId="holding-count"
            label="Holding"
            value={
              !hasWalletSnapshot || data.summary.holdingCount == null
                ? '—'
                : String(data.summary.holdingCount)
            }
            caption="Positive crypto balances"
          />
        </Stack>
      </Paper>
    );
  }
}
