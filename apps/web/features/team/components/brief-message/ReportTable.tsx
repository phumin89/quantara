import { PureComponent, type ComponentProps } from 'react';
import { Box } from '@mui/material';

export default class ReportTable extends PureComponent<
  ComponentProps<'table'>
> {
  public override render() {
    const label = this.props['aria-label'] ?? 'Report table';

    return (
      <Box
        role="region"
        aria-label={label + ' horizontal scroll'}
        tabIndex={0}
        sx={{
          width: '100%',
          minWidth: 0,
          overflowX: 'auto',
          my: 2,
          borderRadius: 'var(--radius-control)',
          '&:focus-visible': {
            outline: '2px solid',
            outlineColor: 'info.main',
            outlineOffset: 2,
          },
        }}
      >
        <Box
          component="table"
          aria-label={label}
          sx={{
            width: '100%',
            minWidth: 520,
            borderCollapse: 'collapse',
            fontSize: 14,
            lineHeight: 1.5,
            textAlign: 'left',
            '& th, & td': {
              px: 2,
              py: 1.25,
              borderBottom: 1,
              borderColor: 'divider',
              verticalAlign: 'top',
            },
            '& th': {
              bgcolor: 'primary.main',
              color: 'primary.contrastText',
              fontWeight: 750,
            },
            '& td': { overflowWrap: 'anywhere' },
            '& tbody tr:nth-of-type(even)': { bgcolor: 'var(--surface-soft)' },
          }}
        >
          {this.props.children}
        </Box>
      </Box>
    );
  }
}
