import type { DailyBrief } from '../contracts/DailyBrief';
import type { TeamOverview } from '../contracts/TeamOverview';

export interface TeamActivityState {
  readonly date: string;
  readonly overview: TeamOverview | null;
  readonly brief: DailyBrief | null;
  readonly loading: boolean;
  readonly error: string | null;
  readonly fetchedAt: number | null;
  readonly now: number;
  readonly accountKey: string | null;
  readonly scopeRevision: number;
}
