import type { IRequestService } from '../../../shared/services/IRequestService';
import {
  riskOverviewSchema,
  type RiskOverview,
} from '../contracts/RiskOverview';
import type { RiskPolicy } from '../contracts/RiskPolicy';
import type { IRiskService } from './IRiskService';

export class RiskService implements IRiskService {
  public constructor(private readonly requests: IRequestService) {}

  public async get(signal?: AbortSignal): Promise<RiskOverview> {
    return riskOverviewSchema.parse(
      await this.requests.get(
        '/crypto/risk',
        'Unable to load risk policy.',
        signal,
      ),
    );
  }

  public async refresh(signal?: AbortSignal): Promise<RiskOverview> {
    return riskOverviewSchema.parse(
      await this.requests.send(
        '/crypto/risk/refresh',
        {},
        'Unable to refresh risk evidence.',
        signal,
      ),
    );
  }

  public async save(
    policy: RiskPolicy,
    expectedPolicyHash: string,
    signal?: AbortSignal,
  ): Promise<RiskOverview> {
    return riskOverviewSchema.parse(
      await this.requests.send(
        '/crypto/risk/policy',
        { policy, expectedPolicyHash },
        'Unable to save risk policy.',
        signal,
      ),
    );
  }
}
