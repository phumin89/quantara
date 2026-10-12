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
      color: '#525252',
      selected: {
        bgcolor: '#fff',
        color: '#141414',
        boxShadow: 'inset 0 0 0 1px #bdbdbd',
      },
      icon: GridViewOutlinedIcon,
    },
    [PortfolioFilter.Holding]: {
      color: '#166534',
      selected: {
        bgcolor: '#dcfce7',
        color: '#166534',
        boxShadow: 'inset 0 0 0 1px #166534',
      },
      icon: AccountBalanceWalletOutlinedIcon,
    },
    [PortfolioFilter.Closed]: {
      color: '#6b21a8',
      selected: {
        bgcolor: '#f3e8ff',
        color: '#6b21a8',
        boxShadow: 'inset 0 0 0 1px #6b21a8',
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
          bgcolor: '#f3f3f3',
          borderRadius: '6px',
          p: 0.5,
          width: 'fit-content',
          '& .MuiToggleButtonGroup-grouped': {
            border: 0,
            borderRadius: '6px',
            minHeight: 44,
            px: 1.25,
            gap: 0.75,
            textTransform: 'none',
            fontSize: 14,
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
