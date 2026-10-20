import type { TeamOverview } from '../../../team/contracts/TeamOverview';
import type { TeamRun } from '../../../team/contracts/TeamRun';
import type { TeamRole } from '../../../team/contracts/TeamRole';

export interface HomeScreenState {
  readonly overview: TeamOverview | null;
  readonly runs: readonly TeamRun[];
  readonly loading: boolean;
  readonly error: string | null;
  readonly fetchedAt: number | null;
  readonly now: number;
  readonly selectedRole: TeamRole;
  readonly paused: boolean;
}
