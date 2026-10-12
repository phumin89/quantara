import type { PortfolioFilter } from '../contracts/PortfolioFilter';

export interface PortfolioFiltersProps {
  filter: PortfolioFilter;
  onChange: (filter: PortfolioFilter) => void;
}
