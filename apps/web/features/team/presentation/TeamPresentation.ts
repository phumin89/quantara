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
      role: TeamRole.Intraday,
      name: 'Intraday Analyst',
      initials: 'IA',
      responsibility: 'Short-horizon market research',
      foreground: '#1e40af',
      background: '#dbeafe',
    },
    {
      role: TeamRole.Swing,
      name: 'Swing Analyst',
      initials: 'SA',
      responsibility: 'Multi-day market research',
      foreground: '#6b21a8',
      background: '#f3e8ff',
    },
    {
      role: TeamRole.News,
      name: 'News Verifier',
      initials: 'NV',
      responsibility: 'Source and news verification',
      foreground: '#92400e',
      background: '#fef3c7',
    },
    {
      role: TeamRole.Positions,
      name: 'Position Reviewer',
      initials: 'PR',
      responsibility: 'Account position review',
      foreground: '#166534',
      background: '#dcfce7',
    },
    {
      role: TeamRole.Quant,
      name: 'Quant Researcher',
      initials: 'QR',
      responsibility: 'Quantitative evidence research',
      foreground: '#1e40af',
      background: '#dbeafe',
    },
    {
      role: TeamRole.Reviewer,
      name: 'Evidence Reviewer',
      initials: 'ER',
      responsibility: 'Evidence and risk review',
      foreground: '#166534',
      background: '#dcfce7',
    },
  ] as const;

  public static readonly legacyMembers = [
    {
      role: TeamRole.Analyst,
      name: 'Analyst',
      initials: 'A',
      responsibility: 'Portfolio research & analysis',
      foreground: '#6b21a8',
      background: '#f3e8ff',
    },
  ] as const;

  public static roleName(role: TeamRole): string {
    return (
      this.members.find((member) => member.role === role)?.name ??
      this.legacyMembers.find((member) => member.role === role)?.name ??
      role
    );
  }

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
