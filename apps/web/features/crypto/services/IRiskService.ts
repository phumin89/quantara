import type { RiskOverview } from '../contracts/RiskOverview';
import type { RiskPolicy } from '../contracts/RiskPolicy';

export interface IRiskService {
  get(signal?: AbortSignal): Promise<RiskOverview>;
  refresh(signal?: AbortSignal): Promise<RiskOverview>;
  save(
    policy: RiskPolicy,
    expectedPolicyHash: string,
    signal?: AbortSignal,
  ): Promise<RiskOverview>;
}
