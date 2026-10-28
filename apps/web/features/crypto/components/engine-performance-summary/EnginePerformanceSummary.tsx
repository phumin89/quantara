import { PureComponent } from 'react';
import { Box, Paper, Typography } from '@mui/material';
import FormatMoney from '../../utils/FormatMoney';
import FormatTime from '../../utils/FormatTime';
import ProfitLossAppearance from '../../presentation/ProfitLossAppearance';
import type { EnginePerformanceSummaryProps } from './EnginePerformanceSummaryProps';

export default class EnginePerformanceSummary extends PureComponent<EnginePerformanceSummaryProps> {
  public override render(): React.ReactNode {
    const { performance } = this.props.data;

    return (
      <Paper
        component="section"
        aria-label="Engine performance"
        variant="outlined"
        sx={{
          p: 'var(--panel-padding)',
          borderColor: 'var(--border-panel)',
          borderRadius: 'var(--radius-panel)',
          bgcolor: 'var(--surface-panel)',
          boxShadow: 'var(--shadow-panel)',
        }}
      >
        <Typography
          component="h2"
          sx={{ fontSize: 16, fontWeight: 650 }}
        >
          Engine P/L
        </Typography>
        <Typography
          variant="body2"
          sx={{ mt: 0.5, color: 'text.secondary' }}
        >
          Confirmed engine fills only. Account-wide profit unavailable until
          full history and cash flows are verified.
        </Typography>
        {performance ? (
          <>
            <Box
              sx={{
                display: 'grid',
                gridTemplateColumns: {
                  xs: 'repeat(2, minmax(0, 1fr))',
                  sm: 'repeat(4, minmax(0, 1fr))',
                },
                gap: 2,
                mt: 2,
              }}
            >
              {this.metric(
                'engine-realized-pnl',
                'Realized',
                performance.realizedPnl,
              )}
              {this.metric(
                'engine-unrealized-pnl',
                'Unrealized',
                performance.unrealizedPnl,
              )}
              {this.metric('engine-total-pnl', 'Net P/L', performance.totalPnl)}
              {this.metric('engine-fees', 'Fees paid', performance.fees, false)}
            </Box>
            <Typography
              variant="body2"
              sx={{ mt: 1.5, color: 'text.secondary' }}
            >
              {performance.notice}
            </Typography>
            {performance.unreconciledSymbols.length > 0 && (
              <Typography
                variant="body2"
                sx={{ mt: 0.5, color: 'warning.main' }}
              >
                Holdings not reconciled:{' '}
                {performance.unreconciledSymbols.join(', ')}.
              </Typography>
            )}
            {performance.asOf && (
              <Typography
                variant="caption"
                sx={{ mt: 0.5, display: 'block', color: 'text.secondary' }}
              >
                Evidence as of {FormatTime.format(performance.asOf)}
              </Typography>
            )}
          </>
        ) : (
          <Typography
            variant="body2"
            sx={{ mt: 1.5, color: 'warning.main' }}
          >
            Engine performance unavailable. Unknown amounts are not zero.
          </Typography>
        )}
      </Paper>
    );
  }

  private metric(
    testId: string,
    label: string,
    value: number | null,
    isProfitLoss: boolean = true,
  ): React.ReactNode {
    return (
      <Box sx={{ minWidth: 0 }}>
        <Typography
          sx={{ fontSize: 12, color: 'text.secondary', fontWeight: 600 }}
        >
          {label}
        </Typography>
        <Typography
          data-testid={testId}
          sx={{
            mt: 0.5,
            fontSize: 18,
            fontWeight: 650,
            fontVariantNumeric: 'tabular-nums',
            color: isProfitLoss
              ? ProfitLossAppearance.color(value)
              : 'text.primary',
          }}
        >
          {isProfitLoss ? FormatMoney.pnl(value) : FormatMoney.format(value)}
        </Typography>
      </Box>
    );
  }
}
