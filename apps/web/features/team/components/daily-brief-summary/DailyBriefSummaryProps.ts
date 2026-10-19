import type { DailyBrief } from '../../contracts/DailyBrief';

export interface DailyBriefSummaryProps {
  readonly brief: DailyBrief | null;
  readonly error: string | null;
}
