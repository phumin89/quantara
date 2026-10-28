import { PureComponent } from 'react';
import { Box, Typography } from '@mui/material';
import type { SummaryMetricProps } from './SummaryMetricProps';

export default class SummaryMetric extends PureComponent<SummaryMetricProps> {
  public override render(): React.ReactNode {
    const { testId, label, value, caption } = this.props;

    return (
      <Box
        sx={{
          flex: 1,
          minWidth: 0,
          px: { xs: 0, sm: 2 },
          py: { xs: 1, sm: 0 },
        }}
      >
        <Typography
          sx={{ fontSize: 12, color: 'text.secondary', fontWeight: 500 }}
        >
          {label}
        </Typography>
        <Typography
          data-testid={testId}
          sx={{
            mt: 0.5,
            fontFamily: 'var(--font-body)',
            fontSize: 28,
            lineHeight: 1.25,
            fontWeight: 600,
            fontVariantNumeric: 'tabular-nums',
            overflowWrap: 'anywhere',
          }}
        >
          {value}
        </Typography>
        <Typography sx={{ mt: 0.5, fontSize: 12, color: 'text.secondary' }}>
          {caption}
        </Typography>
      </Box>
    );
  }
}
