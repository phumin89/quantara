import { PureComponent } from 'react';
import { ToggleButton, ToggleButtonGroup } from '@mui/material';
import GridViewOutlinedIcon from '@mui/icons-material/GridViewOutlined';
import AccountBalanceWalletOutlinedIcon from '@mui/icons-material/AccountBalanceWalletOutlined';
import CheckCircleOutlinedIcon from '@mui/icons-material/CheckCircleOutlined';
import { PortfolioFilter } from '../../contracts/PortfolioFilter';
import type { PortfolioFiltersProps } from './PortfolioFiltersProps';

export default class PortfolioFilters extends PureComponent<PortfolioFiltersProps> {
  private static readonly filterOptions = Object.values(PortfolioFilter);

  private static readonly filterStyles = {
    [PortfolioFilter.All]: {
      color: 'text.secondary',
      selected: {
        bgcolor: 'var(--surface-raised)',
        color: 'text.primary',
        boxShadow: 'inset 0 0 0 1px var(--border-panel)',
      },
      icon: GridViewOutlinedIcon,
    },
    [PortfolioFilter.Holding]: {
      color: 'success.main',
      selected: {
        bgcolor: 'var(--surface-success)',
        color: 'success.main',
        boxShadow: 'inset 0 0 0 1px currentColor',
      },
      icon: AccountBalanceWalletOutlinedIcon,
    },
    [PortfolioFilter.Closed]: {
      color: 'secondary.main',
      selected: {
        bgcolor: 'var(--surface-warning)',
        color: 'secondary.main',
        boxShadow: 'inset 0 0 0 1px currentColor',
      },
      icon: CheckCircleOutlinedIcon,
    },
  } as const;

  public override render(): React.ReactNode {
    const { filter, onChange } = this.props;

    return (
      <ToggleButtonGroup
        exclusive
        value={filter}
        onChange={(_, value: PortfolioFilter | null) => {
          if (value) {
            onChange(value);
          }
        }}
        aria-label="Portfolio filter"
        size="small"
        sx={{
          bgcolor: 'var(--surface-soft)',
          borderRadius: 'var(--radius-control)',
          border: '1px solid',
          borderColor: 'var(--border-panel)',
          p: 0.75,
          gap: 0.5,
          width: 'fit-content',
          maxWidth: '100%',
          '& .MuiToggleButtonGroup-grouped': {
            border: 0,
            borderRadius: 'var(--radius-control)',
            minHeight: 44,
            px: 1.25,
            gap: 0.75,
            textTransform: 'none',
            fontSize: 14,
            fontWeight: 800,
          },
        }}
      >
        {PortfolioFilters.filterOptions.map((value) => {
          const option = PortfolioFilters.filterStyles[value];
          const Icon = option.icon;

          return (
            <ToggleButton
              key={value}
              value={value}
              sx={{
                border: 0,
                color: option.color,
                '&.Mui-selected': option.selected,
              }}
            >
              <Icon
                aria-hidden="true"
                sx={{ fontSize: 18 }}
              />
              {value}
            </ToggleButton>
          );
        })}
      </ToggleButtonGroup>
    );
  }
}
