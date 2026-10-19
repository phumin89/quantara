import type { TeamRun } from '../../contracts/TeamRun';
import type { TeamOverview } from '../../contracts/TeamOverview';

export interface TeamMembersProps {
  readonly latest: TeamRun | undefined;
  readonly overview: TeamOverview | null;
  readonly loading: boolean;
  readonly membersOpen: boolean;
  readonly onToggle: () => void;
}
