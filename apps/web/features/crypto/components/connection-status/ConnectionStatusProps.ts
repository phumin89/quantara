import type { PortfolioResponse } from '../../contracts/PortfolioResponse';

export interface ConnectionStatusProps {
  data: PortfolioResponse | null;
  error: string | null;
  refreshing: boolean;
}
