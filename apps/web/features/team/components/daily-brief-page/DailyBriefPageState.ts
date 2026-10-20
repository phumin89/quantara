import type { DailyBrief } from '../../contracts/DailyBrief';
import type { DailyBriefView } from './DailyBriefView';
import type { NewsPacket } from '../../contracts/NewsPacket';

export interface DailyBriefPageState {
  readonly date: string;
  readonly brief: DailyBrief | null;
  readonly scopeRevision: number;
  readonly newsScopeRevision: number;
  readonly loading: boolean;
  readonly error: string | null;
  readonly membersOpen: boolean;
  readonly controlling: boolean;
  readonly view: DailyBriefView;
  readonly news: NewsPacket | null;
  readonly newsError: string | null;
}
