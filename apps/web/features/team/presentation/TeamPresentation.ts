import { TeamRole } from '../contracts/TeamRole';
import { TeamRunStatus } from '../contracts/TeamRunStatus';

export class TeamPresentation {
  public static readonly members = [
    {
      role: TeamRole.Ceo,
      name: 'CEO',
      initials: 'C',
      responsibility: 'Direction & coordination',
      foreground: '#1e40af',
      background: '#dbeafe',
    },
    {
      role: TeamRole.Analyst,
      name: 'Analyst',
      initials: 'A',
      responsibility: 'Portfolio research & analysis',
      foreground: '#6b21a8',
      background: '#f3e8ff',
    },
    {
      role: TeamRole.Reviewer,
      name: 'Reviewer',
      initials: 'R',
      responsibility: 'Evidence & risk review',
      foreground: '#166534',
      background: '#dcfce7',
    },
  ] as const;

  public static statusStyle(status: TeamRunStatus | undefined) {
    switch (status) {
      case TeamRunStatus.Completed:
        return { color: '#166534', bgcolor: '#dcfce7' };
      case TeamRunStatus.Queued:
      case TeamRunStatus.Running:
        return { color: '#92400e', bgcolor: '#fef3c7' };
      case TeamRunStatus.Failed:
        return { color: '#991b1b', bgcolor: '#fee2e2' };
      case TeamRunStatus.Cancelled:
      case undefined:
        return { color: '#525252', bgcolor: '#f3f3f3' };
    }
  }
}
