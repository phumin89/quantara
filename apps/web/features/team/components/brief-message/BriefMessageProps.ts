import type { TeamRole } from '../../contracts/TeamRole';

export interface BriefMessageProps {
  readonly output: string;
  readonly idPrefix?: string;
  readonly role?: TeamRole;
}
