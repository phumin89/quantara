import type { TeamOverview } from '../contracts/TeamOverview';
import type { DailyBrief } from '../contracts/DailyBrief';

export class TeamActivitySnapshot {
  public static today(at: number = Date.now()): string {
    return new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Bangkok',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date(at));
  }

  public static scope(overview: TeamOverview, brief: DailyBrief): DailyBrief {
    const latest = overview.latestRun;

    return {
      date: brief.date,
      runs: latest
        ? brief.runs.filter(
            (run) => (run.accountKey ?? null) === (latest.accountKey ?? null),
          )
        : [],
    };
  }

  public static isOlder(
    previous: TeamOverview | null,
    next: TeamOverview,
  ): boolean {
    const before = previous?.latestRun;
    const after = next.latestRun;
    if (!before || !after || before.accountKey !== after.accountKey) {
      return false;
    }

    if (before.id === after.id && before.updatedAt === after.updatedAt) {
      return JSON.stringify(before) !== JSON.stringify(after);
    }

    return before.id === after.id
      ? Date.parse(after.updatedAt) < Date.parse(before.updatedAt)
      : Date.parse(after.createdAt) < Date.parse(before.createdAt);
  }
}
