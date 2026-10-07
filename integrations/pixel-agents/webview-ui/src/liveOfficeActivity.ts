import { TYPE_FRAME_DURATION_SEC } from './constants.js';
import type { OfficeState } from './office/engine/officeState.js';
import { getCatalogEntry } from './office/layout/furnitureCatalog.js';
import { getLoadedCharacterCount } from './office/sprites/spriteData.js';
import { CharacterState, TILE_SIZE } from './office/types.js';

export const LIVE_ACTIVITY_ORIGIN = 'https://localhost:3000';
export const LIVE_ACTIVITY_MAX_AGE_MS = 45_000;
export const LIVE_ACTIVITY_ROLES = [
  'ceo',
  'chief-of-staff',
  'market-research',
  'security-analysis',
  'quant-research',
  'portfolio-initial',
  'risk-critic',
  'portfolio-final',
] as const;

type LiveRole = (typeof LIVE_ACTIVITY_ROLES)[number];
type LiveStatus = 'idle' | 'working' | 'completed' | 'failed';

export interface LiveOfficeActivity {
  accountContext: { mode: 1; accountId: string };
  meetingId: string | null;
  observedAt: string;
  roles: Array<{ role: LiveRole; status: LiveStatus; changedAt: string }>;
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function timestamp(value: unknown): number {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(value)) return NaN;
  const localTime = value.slice(0, 19);
  const localTimestamp = Date.parse(`${localTime}Z`);
  if (!Number.isFinite(localTimestamp) || new Date(localTimestamp).toISOString().slice(0, 19) !== localTime) return NaN;
  return Date.parse(value);
}

export function isLiveOfficeActivityFresh(activity: LiveOfficeActivity, now: number): boolean {
  const age = now - Date.parse(activity.observedAt);
  return age >= -5_000 && age < LIVE_ACTIVITY_MAX_AGE_MS;
}

export function readLiveOfficeActivity(value: unknown, now: number): LiveOfficeActivity | null {
  if (!record(value) || !record(value.accountContext)) return null;
  const context = value.accountContext;
  if (context.mode !== 1 || typeof context.accountId !== 'string'
    || !/^[0-9a-f]{32}$/i.test(context.accountId) || /^0{32}$/.test(context.accountId)) return null;
  if (value.meetingId !== null && (typeof value.meetingId !== 'string'
    || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.meetingId)
    || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value.meetingId))) return null;
  const observedAt = timestamp(value.observedAt);
  if (!Number.isFinite(observedAt) || observedAt > now + 5_000 || now - observedAt >= LIVE_ACTIVITY_MAX_AGE_MS) return null;
  if (!Array.isArray(value.roles) || value.roles.length > LIVE_ACTIVITY_ROLES.length) return null;
  if (value.meetingId === null && value.roles.length > 0) return null;

  const roles: LiveOfficeActivity['roles'] = [];
  const seen = new Set<string>();
  let working = 0;
  for (const role of value.roles) {
    if (!record(role) || typeof role.role !== 'string'
      || !LIVE_ACTIVITY_ROLES.includes(role.role as LiveRole) || seen.has(role.role)
      || !['idle', 'working', 'completed', 'failed'].includes(role.status as string)) return null;
    const changedAt = timestamp(role.changedAt);
    if (!Number.isFinite(changedAt) || changedAt > observedAt) return null;
    if (role.status === 'working' && ++working > 1) return null;
    seen.add(role.role);
    roles.push({ role: role.role as LiveRole, status: role.status as LiveStatus, changedAt: role.changedAt as string });
  }
  return {
    accountContext: { mode: 1, accountId: context.accountId },
    meetingId: value.meetingId,
    observedAt: value.observedAt as string,
    roles,
  };
}

/** null ignores an unrelated sender/message; a null activity clears the view. */
export function readLiveOfficeMessage(
  event: { source: unknown; origin: string; data: unknown },
  parent: unknown,
  viewer: unknown,
  now: number,
): { activity: LiveOfficeActivity | null } | null {
  if (parent === viewer || event.source !== parent || event.origin !== LIVE_ACTIVITY_ORIGIN) return null;
  if (!record(event.data) || event.data.type !== 'quantara-live-activity') return null;
  return { activity: readLiveOfficeActivity(event.data.activity, now) };
}

export function applyLiveOfficeActivity(state: OfficeState, activity: LiveOfficeActivity | null): void {
  const reported = new Map(activity?.roles.map(role => [role.role, role]) ?? []);
  // Staff presence is configuration; work status must come from fresh telemetry.
  const roles = activity ? LIVE_ACTIVITY_ROLES.map(role => reported.get(role) ?? {
    role,
    status: 'idle' as const,
  }) : [];
  const ids = new Set(roles.map(({ role }) => LIVE_ACTIVITY_ROLES.indexOf(role) + 1));
  for (const id of state.characters.keys()) {
    if (ids.has(id)) continue;
    state.setAgentActive(id, false);
    state.removeAgent(id);
    // Live activity clears immediately; the generic office uses a despawn effect.
    state.characters.delete(id);
  }

  const seatPriority = (id: string) => {
    const furniture = state.layout.furniture.find((item) => id === item.uid || id.startsWith(`${item.uid}:`));
    const label = furniture ? getCatalogEntry(furniture.type)?.label.toLowerCase() ?? '' : '';
    if (label.includes('chair')) return 0;
    if (label.includes('bench')) return 1;
    // Prefer different sofas before adjacent cells on the same vertical sofa.
    return furniture && id !== furniture.uid ? 3 : 2;
  };
  const seats = [...state.seats.keys()].sort().sort((left, right) => seatPriority(left) - seatPriority(right));
  for (const role of roles) {
    const index = LIVE_ACTIVITY_ROLES.indexOf(role.role);
    const id = index + 1;
    const seatId = seats[index];
    if (!seatId) continue;
    state.addAgent(id, index % Math.max(1, getLoadedCharacterCount()), 0, seatId, true);
    const character = state.characters.get(id);
    const seat = state.seats.get(seatId);
    if (!character || !seat) continue;
    const active = role.status === 'working';
    const changed = character.isActive !== active;
    state.setAgentActive(id, active);
    character.agentName = role.role;
    character.state = active ? CharacterState.TYPE : CharacterState.IDLE;
    character.dir = seat.facingDir;
    character.tileCol = seat.seatCol;
    character.tileRow = seat.seatRow;
    character.x = seat.seatCol * TILE_SIZE + TILE_SIZE / 2;
    character.y = seat.seatRow * TILE_SIZE + TILE_SIZE / 2;
    character.path = [];
    character.moveProgress = 0;
    if (changed || !active) {
      character.frame = 0;
      character.frameTimer = 0;
    }
  }
}

/** Advance typing only: OfficeState.update also wanders idle agents and pets. */
export function animateLiveOfficeWork(state: OfficeState, elapsedSeconds: number): void {
  for (const character of state.characters.values()) {
    if (!character.isActive) continue;
    character.frameTimer += elapsedSeconds;
    const frames = Math.floor(character.frameTimer / TYPE_FRAME_DURATION_SEC);
    character.frame = (character.frame + frames) % 2;
    character.frameTimer %= TYPE_FRAME_DURATION_SEC;
  }
}
