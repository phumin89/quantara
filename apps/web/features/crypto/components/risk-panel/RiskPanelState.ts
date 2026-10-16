import type { RiskOverview } from '../../contracts/RiskOverview';
import type { RiskPolicy } from '../../contracts/RiskPolicy';

export interface RiskPanelState {
  readonly overview: RiskOverview | null;
  readonly policy: RiskPolicy | null;
  readonly loading: boolean;
  readonly busy: boolean;
  readonly error: string | null;
  readonly basePolicyHash: string | null;
  readonly dirty: boolean;
}
