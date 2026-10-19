import type { NewsPacket } from '../../contracts/NewsPacket';

export interface DailyBriefNewsProps {
  readonly news: NewsPacket | null;
  readonly error: string | null;
}
