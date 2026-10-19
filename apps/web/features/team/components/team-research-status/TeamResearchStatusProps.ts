import type { CompanyLoopOverview } from '../../contracts/CompanyLoopOverview';

export interface TeamResearchStatusProps {
  readonly company: CompanyLoopOverview | null | undefined;
  readonly loading: boolean;
  readonly controlling: boolean;
  readonly onControl: () => void;
}
