import type { TeamRole } from '../../../team/contracts/TeamRole';

export interface HomeScreenState {
  readonly selectedRole: TeamRole;
  readonly paused: boolean;
}
