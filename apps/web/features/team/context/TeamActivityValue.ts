import type { TeamActivityState } from './TeamActivityState';
import type { ITeamService } from '../services/ITeamService';

export interface TeamActivityValue extends TeamActivityState {
  readonly service: ITeamService;
  readonly refresh: (force?: boolean) => Promise<void>;
}
