import { TeamRole } from '../../team/contracts/TeamRole';
import { TeamRunStatus } from '../../team/contracts/TeamRunStatus';
import type { TeamOverview } from '../../team/contracts/TeamOverview';
import type { TeamRun } from '../../team/contracts/TeamRun';
import type { OfficeAgent } from '../contracts/OfficeAgent';
import { OfficeAgentStatus } from '../contracts/OfficeAgentStatus';

export class OfficeStateAdapter {
  public static readonly roles = [
    TeamRole.Ceo,
    TeamRole.Analyst,
    TeamRole.Reviewer,
    TeamRole.Intraday,
    TeamRole.Swing,
    TeamRole.News,
    TeamRole.Positions,
    TeamRole.Quant,
  ];

  private static readonly labels: Record<TeamRole, string> = {
    ceo: 'CEO',
    analyst: 'Analyst',
    reviewer: 'Reviewer',
    intraday: 'Intraday',
    swing: 'Swing',
    news: 'News',
    positions: 'Positions',
    quant: 'Quant',
  };

  public static project(
    overview: TeamOverview | null,
    runs: readonly TeamRun[],
    fresh: boolean,
  ): readonly OfficeAgent[] {
    const retained = new Map<string, TeamRun>();
    const accountKey = overview?.latestRun?.accountKey;
    for (const run of [
      ...(overview?.latestRun ? runs : []),
      ...(overview?.latestRun ? [overview.latestRun] : []),
    ]) {
      if ((run.accountKey ?? null) !== (accountKey ?? null)) {
        continue;
      }

      const previous = retained.get(run.id);
      if (
        !previous ||
        Date.parse(run.updatedAt) >= Date.parse(previous.updatedAt)
      ) {
        retained.set(run.id, run);
      }
    }

    const ordered = [...retained.values()].sort(
      (left, right) => Date.parse(right.updatedAt) - Date.parse(left.updatedAt),
    );

    return this.roles.map((role, index) => {
      const run = ordered.find((item) =>
        item.steps.some((step) => step.role === role),
      );
      const step = run?.steps.find((item) => item.role === role);
      let status = OfficeAgentStatus.Idle;
      if (!fresh) {
        status = run ? OfficeAgentStatus.Stale : OfficeAgentStatus.Unknown;
      } else if (overview?.configured === false) {
        status = OfficeAgentStatus.Blocked;
      } else if (step) {
        status = this.status(step.status);
        if (step.status === TeamRunStatus.Running) {
          if (
            run?.status !== TeamRunStatus.Running ||
            run.id !== overview?.latestRun?.id
          ) {
            status = OfficeAgentStatus.Unknown;
          } else if (overview.company?.enabled === false) {
            status = OfficeAgentStatus.Blocked;
          }
        }
      }

      return {
        id: index + 1,
        role,
        label: this.labels[role],
        status,
        updatedAt: run?.updatedAt ?? null,
        runId: run?.id ?? null,
        date: run ? this.date(run.createdAt) : null,
        summary:
          this.clean(step?.error ?? step?.output ?? run?.error ?? '') || null,
      };
    });
  }

  public static isOlder(
    previous: TeamOverview | null,
    next: TeamOverview,
  ): boolean {
    const before = previous?.latestRun;
    const after = next.latestRun;
    if (!before) {
      return false;
    }

    if (!after || before.accountKey !== after.accountKey) {
      return false;
    }

    if (before.id === after.id && before.updatedAt === after.updatedAt) {
      return JSON.stringify(before) !== JSON.stringify(after);
    }

    return before.id === after.id
      ? Date.parse(after.updatedAt) < Date.parse(before.updatedAt)
      : Date.parse(after.createdAt) < Date.parse(before.createdAt);
  }

  public static date(at: string | number = Date.now()): string {
    return new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Bangkok',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date(at));
  }

  public static clean(value: string): string {
    return value
      .replace(/[\u0000-\u001f\u007f]/g, ' ')
      .slice(0, 700)
      .trim();
  }

  private static status(status: TeamRunStatus): OfficeAgentStatus {
    switch (status) {
      case TeamRunStatus.Running:
        return OfficeAgentStatus.Active;
      case TeamRunStatus.Queued:
        return OfficeAgentStatus.Queued;
      case TeamRunStatus.Completed:
        return OfficeAgentStatus.Completed;
      case TeamRunStatus.Failed:
        return OfficeAgentStatus.Failed;
      case TeamRunStatus.Cancelled:
        return OfficeAgentStatus.Cancelled;
    }
  }
}
