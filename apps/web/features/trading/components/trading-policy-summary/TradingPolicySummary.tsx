import { Component, type ReactNode } from 'react';
import { Box, Stack, Typography } from '@mui/material';
import type { TradingPolicySummaryProps } from './TradingPolicySummaryProps';

export default class TradingPolicySummary extends Component<TradingPolicySummaryProps> {
  public override render(): ReactNode {
    const rules = this.props.overview.rules;
    const adaptive = rules.adaptive;
    if (!adaptive) {
      return null;
    }

    const experimental = rules.version === 'adaptive-intraday-v3';

    return (
      <Stack spacing={2}>
        <Typography
          component="h3"
          sx={{ fontSize: 15, fontWeight: 700 }}
        >
          System-managed trading plan
        </Typography>
        <Box
          component="dl"
          sx={{
            m: 0,
            fontSize: 14,
            '& dt': { fontWeight: 600 },
            '& dd': { m: 0, mb: 1.5, color: 'text.secondary' },
          }}
        >
          <Box component="dt">Markets</Box>
          <Box component="dd">
            Eligible Bitkub THB spot markets
            {adaptive.excludedSymbols.length > 0 &&
              `, excluding ${adaptive.excludedSymbols.join(', ')}`}
            .
          </Box>
          <Box component="dt">Order size</Box>
          <Box component="dd">
            Calculated from verified capital, available funds and saved risk
            limits.
          </Box>
          <Box component="dt">Stops, targets &amp; trailing</Box>
          <Box component="dd">
            Price-structure stops; volatility-based targets and trailing within
            saved strategy rules.
          </Box>
          <Box component="dt">Holding time</Box>
          <Box component="dd">
            {experimental
              ? 'Experimental v3 uses its saved exit policy for review intervals and maximum holding times.'
              : `Saved maximum holding time: ${String(rules.timeExitMinutes)} minutes.`}
          </Box>
        </Box>
        <Typography
          component="h3"
          sx={{ fontSize: 15, fontWeight: 700 }}
        >
          Saved risk limits
        </Typography>
        <Box
          component="dl"
          sx={{
            m: 0,
            display: 'grid',
            gridTemplateColumns: 'minmax(0, 1fr) auto',
            columnGap: 2,
            rowGap: 0.75,
            fontSize: 14,
          }}
        >
          {(
            [
              ['Maximum exposure', adaptive.risk.maximumExposureFraction],
              [
                'Maximum concentration',
                adaptive.risk.maximumConcentrationFraction,
              ],
              ['Risk to stop scenario', adaptive.risk.riskToStopFraction],
              ['Daily loss cap on current equity', adaptive.dailyLossFraction],
            ] as const
          ).map(([label, value]) => (
            <Box
              key={label}
              sx={{ display: 'contents' }}
            >
              <Box component="dt">{label}</Box>
              <Box
                component="dd"
                sx={{
                  m: 0,
                  textAlign: 'right',
                  fontVariantNumeric: 'tabular-nums',
                }}
              >
                {value.toLocaleString('en-GB', {
                  style: 'percent',
                  maximumFractionDigits: 20,
                })}
              </Box>
            </Box>
          ))}
        </Box>
        <Typography
          variant="body2"
          color="text.secondary"
        >
          {experimental
            ? 'Experimental v3 · exit policy is read-only. Live trading remains subject to the server’s eligibility checks and owner mandate.'
            : `Policy: ${rules.version}. Risk limits are scenarios, not a guaranteed loss cap.`}
        </Typography>
      </Stack>
    );
  }
}
