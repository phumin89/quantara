import type { DailyBrief } from '../../contracts/DailyBrief';
import type { NewsPacket } from '../../contracts/NewsPacket';

export interface DailyBriefPageState {
  readonly date: string;
  readonly brief: DailyBrief | null;
  readonly scopeRevision: number;
  readonly newsScopeRevision: number;
  readonly loading: boolean;
  readonly error: string | null;
  readonly controlling: boolean;
  readonly news: NewsPacket | null;
  readonly newsError: string | null;
}
