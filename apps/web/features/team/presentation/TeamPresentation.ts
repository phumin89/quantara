import { TeamRole } from '../contracts/TeamRole';
import { TeamRunStatus } from '../contracts/TeamRunStatus';

export class TeamPresentation {
  public static readonly members = [
    {
      role: TeamRole.Ceo,
      name: 'CEO',
      initials: 'C',
      responsibility: 'Direction & coordination',
      foreground: 'var(--color-info)',
      background: 'var(--surface-info)',
    },
    {
      role: TeamRole.Intraday,
      name: 'Intraday Analyst',
      initials: 'IA',
      responsibility: 'Short-horizon market research',
      foreground: 'var(--color-info)',
      background: 'var(--surface-info)',
    },
    {
      role: TeamRole.Swing,
      name: 'Swing Analyst',
      initials: 'SA',
      responsibility: 'Multi-day market research',
      foreground: 'var(--color-info)',
      background: 'var(--surface-info)',
    },
    {
      role: TeamRole.News,
      name: 'News Verifier',
      initials: 'NV',
      responsibility: 'Source and news verification',
      foreground: 'var(--color-warning)',
      background: 'var(--surface-warning)',
    },
    {
      role: TeamRole.Positions,
      name: 'Position Reviewer',
      initials: 'PR',
      responsibility: 'Account position review',
      foreground: 'var(--color-success)',
      background: 'var(--surface-success)',
    },
    {
      role: TeamRole.Quant,
      name: 'Quant Researcher',
      initials: 'QR',
      responsibility: 'Quantitative evidence research',
      foreground: 'var(--color-info)',
      background: 'var(--surface-info)',
    },
    {
      role: TeamRole.Reviewer,
      name: 'Evidence Reviewer',
      initials: 'ER',
      responsibility: 'Evidence and risk review',
      foreground: 'var(--color-success)',
      background: 'var(--surface-success)',
    },
  ] as const;

  public static readonly legacyMembers = [
    {
      role: TeamRole.Analyst,
      name: 'Analyst',
      initials: 'A',
      responsibility: 'Portfolio research & analysis',
      foreground: 'var(--color-info)',
      background: 'var(--surface-info)',
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
        return {
          color: 'var(--color-success)',
          bgcolor: 'var(--surface-success)',
        };
      case TeamRunStatus.Queued:
      case TeamRunStatus.Running:
        return {
          color: 'var(--color-warning)',
          bgcolor: 'var(--surface-warning)',
        };
      case TeamRunStatus.Failed:
        return { color: 'var(--color-error)', bgcolor: 'var(--surface-error)' };
      case TeamRunStatus.Cancelled:
      case undefined:
        return { color: 'var(--color-muted)', bgcolor: 'var(--surface-soft)' };
    }
  }
}
