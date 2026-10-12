import type { TeamOverview } from '../../contracts/TeamOverview';
import type { TeamRun } from '../../contracts/TeamRun';

export interface TeamPageState {
  readonly overview: TeamOverview | null;
  readonly run: TeamRun | null;
  readonly objective: string;
  readonly loading: boolean;
  readonly submitting: boolean;
  readonly stopping: boolean;
  readonly error: string | null;
}
