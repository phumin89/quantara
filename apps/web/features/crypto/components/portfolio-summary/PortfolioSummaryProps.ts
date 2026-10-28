import type { PortfolioResponse } from '../../contracts/PortfolioResponse';

export interface PortfolioSummaryProps {
  data: PortfolioResponse;
  readonly holdingsKnown?: boolean;
}
