import type { DailyBrief } from '../../contracts/DailyBrief';

export interface DailyBriefConversationProps {
  readonly active: boolean;
  readonly brief: DailyBrief | null;
  readonly error: string | null;
  readonly date: string;
  readonly loading: boolean;
}
