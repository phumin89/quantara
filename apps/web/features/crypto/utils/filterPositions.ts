import type { Position } from '../contracts/Position';
import { PortfolioFilter } from '../contracts/PortfolioFilter';
import { PositionStatus } from '../contracts/PositionStatus';

function matchesFilter(
  status: PositionStatus,
  filter: PortfolioFilter,
): boolean {
  switch (filter) {
    case PortfolioFilter.All:
      return true;
    case PortfolioFilter.Holding:
      return status === PositionStatus.Holding;
    case PortfolioFilter.Closed:
      return status === PositionStatus.Closed;
  }
}

export default function filterPositions(
  positions: Position[],
  filter: PortfolioFilter,
): Position[] {
  return positions
    .filter((item) => matchesFilter(item.status, filter))
    .sort(
      (a, b) =>
        Number(b.status === PositionStatus.Holding) -
          Number(a.status === PositionStatus.Holding) ||
        a.symbol.localeCompare(b.symbol),
    );
}
