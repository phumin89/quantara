import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { test } from 'vitest';

import { buildAssetIndex, buildFurnitureCatalog } from '../../core/src/assets/build.js';
import { decodeAllFurniture } from '../../core/src/assets/loader.js';
import {
  animateLiveOfficeWork,
  applyLiveOfficeActivity,
  isLiveOfficeActivityFresh,
  LIVE_ACTIVITY_ORIGIN,
  LIVE_ACTIVITY_ROLES,
  readLiveOfficeActivity,
  readLiveOfficeMessage,
} from '../src/liveOfficeActivity.js';
import { OfficeState } from '../src/office/engine/officeState.js';
import { buildDynamicCatalog } from '../src/office/layout/furnitureCatalog.js';
import { deserializeLayout, migrateLayoutColors } from '../src/office/layout/layoutSerializer.js';
import type { OfficeLayout } from '../src/office/types.js';
import { CharacterState, Direction, TILE_SIZE, TileType } from '../src/office/types.js';

const now = Date.parse('2026-10-06T12:00:00.000Z');
const parent = {};
const viewer = {};

function activity() {
  return {
    accountContext: { mode: 1, accountId: '0123456789abcdef0123456789abcdef' },
    meetingId: '01234567-89ab-cdef-0123-456789abcdef',
    observedAt: new Date(now).toISOString(),
    roles: [{ role: 'ceo', status: 'working', changedAt: new Date(now - 1_000).toISOString() }],
  };
}

function office(): OfficeState {
  const state = new OfficeState({
    version: 1, cols: 12, rows: 8,
    tiles: new Array<TileType>(12 * 8).fill(TileType.FLOOR_1), furniture: [],
  });
  for (let index = 0; index < 8; index++) {
    const uid = `seat-${index}`;
    state.seats.set(uid, { uid, seatCol: index + 1, seatRow: 3, facingDir: Direction.UP, assigned: false });
  }
  return state;
}

function message(value: unknown, origin = LIVE_ACTIVITY_ORIGIN, source: unknown = parent) {
  return readLiveOfficeMessage({ source, origin, data: { type: 'quantara-live-activity', activity: value } }, parent, viewer, now);
}

test('fresh activity shows configured staff and animates only the reported working role', () => {
  const state = office();
  applyLiveOfficeActivity(state, readLiveOfficeActivity(undefined, now));
  assert.equal(state.getCharacters().length, 0);
  const accepted = message(activity());
  assert.ok(accepted?.activity);
  applyLiveOfficeActivity(state, accepted.activity);
  const [character] = state.getCharacters();
  assert.equal(state.getCharacters().length, 8);
  assert.equal(character.id, 1);
  assert.equal(character.agentName, 'ceo');
  assert.equal(character.isActive, true);
  assert.equal(character.state, CharacterState.TYPE);
  assert.equal(character.seatId, 'seat-0');
  assert.equal(character.x, TILE_SIZE + TILE_SIZE / 2);
  assert.equal(character.matrixEffect, null);
});

test('missing, invalid and expired activity immediately clears characters and their seats', () => {
  for (const value of [undefined, null, { ...activity(), observedAt: new Date(now - 45_000).toISOString() }, { ...activity(), observedAt: new Date(now - 45_001).toISOString() }, { ...activity(), meetingId: null, roles: [], observedAt: new Date(now - 45_000).toISOString() }]) {
    const state = office();
    applyLiveOfficeActivity(state, readLiveOfficeActivity(activity(), now));
    const accepted = message(value);
    assert.ok(accepted);
    applyLiveOfficeActivity(state, accepted.activity);
    assert.equal(state.getCharacters().length, 0);
    assert.equal([...state.seats.values()].some((seat) => seat.assigned), false);
  }
});

test('valid empty heartbeat keeps all configured staff inactive', () => {
  const state = office();
  const accepted = message({ ...activity(), meetingId: null, roles: [] });
  assert.ok(accepted?.activity);
  applyLiveOfficeActivity(state, accepted.activity);
  assert.equal(state.getCharacters().length, 8);
  assert.equal(state.getCharacters().filter(character => character.isActive).length, 0);
});

test('a new failed scout snapshot keeps the team at its seats and clears earlier work', () => {
  const state = office();
  applyLiveOfficeActivity(state, readLiveOfficeActivity(activity(), now));
  const positions = state.getCharacters().map(character => [character.id, character.x, character.y]);
  const scout = { ...activity(), meetingId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', roles: [{ ...activity().roles[0], role: 'market-research', status: 'failed' }] };
  applyLiveOfficeActivity(state, readLiveOfficeActivity(scout, now));
  assert.deepEqual(state.getCharacters().map(character => [character.id, character.x, character.y]), positions);
  assert.equal(state.getCharacters().filter(character => character.isActive).length, 0);
  applyLiveOfficeActivity(state, readLiveOfficeActivity({ ...scout, roles: [{ ...scout.roles[0], status: 'working' }] }, now));
  assert.deepEqual(state.getCharacters().filter(character => character.isActive).map(character => character.agentName), ['market-research']);
  assert.equal(state.characters.get(1)?.isActive, false);
});

test('rejects foreign origins, other frames, self messages and standalone viewers', () => {
  assert.equal(message(activity(), 'https://example.com'), null);
  assert.equal(message(activity(), 'http://localhost:3000'), null);
  assert.equal(message(activity(), LIVE_ACTIVITY_ORIGIN, {}), null);
  assert.equal(message(activity(), LIVE_ACTIVITY_ORIGIN, viewer), null);
  assert.equal(readLiveOfficeMessage({ source: viewer, origin: LIVE_ACTIVITY_ORIGIN, data: { type: 'quantara-live-activity', activity: activity() } }, viewer, viewer, now), null);
  const state = office();
  assert.equal(state.getCharacters().length, 0);
});

test('ignores unrelated message types without interpreting their activity', () => {
  assert.equal(readLiveOfficeMessage({ source: parent, origin: LIVE_ACTIVITY_ORIGIN, data: { type: 'agentsLoaded', activity: activity() } }, parent, viewer, now), null);
});

test('malformed, unknown, duplicate and multiple working roles never produce workers', () => {
  const valid = activity();
  const invalid = [
    {}, [], 'working',
    { ...valid, accountContext: { mode: 0, accountId: valid.accountContext.accountId } },
    { ...valid, accountContext: { mode: 1, accountId: 'not-an-account' } },
    { ...valid, accountContext: { mode: 1, accountId: '00000000000000000000000000000000' } },
    { ...valid, meetingId: '' },
    { ...valid, meetingId: null },
    { ...valid, meetingId: '00000000-0000-0000-0000-000000000000' },
    { ...valid, observedAt: 'yesterday' },
    { ...valid, observedAt: '2026-09-31T12:00:00Z' },
    { ...valid, observedAt: new Date(now + 5_001).toISOString() },
    { ...valid, roles: undefined },
    { ...valid, roles: [null] },
    { ...valid, roles: [{ ...valid.roles[0], role: 'made-up-agent' }] },
    { ...valid, roles: [{ ...valid.roles[0], status: 'queued' }] },
    { ...valid, roles: [{ ...valid.roles[0], changedAt: new Date(now + 1).toISOString() }] },
    { ...valid, roles: [{ ...valid.roles[0], changedAt: 'not-a-date' }] },
    { ...valid, roles: [valid.roles[0], valid.roles[0]] },
    { ...valid, roles: [valid.roles[0], { ...valid.roles[0], role: 'risk-critic' }] },
    { ...valid, roles: new Array(9).fill(valid.roles[0]) },
  ];
  for (const value of invalid) {
    const state = office();
    applyLiveOfficeActivity(state, readLiveOfficeActivity(activity(), now));
    const accepted = message(value);
    assert.ok(accepted);
    assert.equal(accepted.activity, null);
    applyLiveOfficeActivity(state, accepted.activity);
    assert.equal(state.getCharacters().length, 0);
  }
});

test('freshness expires at 45 seconds and accepts up to 5 seconds of clock skew', () => {
  for (const offset of [-44_999, 5_000]) {
    const observedAt = new Date(now + offset).toISOString();
    const accepted = readLiveOfficeActivity({ ...activity(), observedAt, roles: [{ ...activity().roles[0], changedAt: observedAt }] }, now);
    assert.ok(accepted);
    assert.equal(isLiveOfficeActivityFresh(accepted, now), true);
  }
  const accepted = readLiveOfficeActivity(activity(), now);
  assert.ok(accepted);
  assert.equal(isLiveOfficeActivityFresh(accepted, now + 44_999), true);
  assert.equal(isLiveOfficeActivityFresh(accepted, now + 45_000), false);
  const observedAt = new Date(now - 45_000).toISOString();
  assert.equal(readLiveOfficeActivity({ ...activity(), observedAt, roles: [{ ...activity().roles[0], changedAt: observedAt }] }, now), null);
  assert.ok(readLiveOfficeActivity({ ...activity(), meetingId: null, roles: [] }, now));
});

test('completed, failed and idle roles remain inactive, static and at their seats', () => {
  const state = office();
  applyLiveOfficeActivity(state, readLiveOfficeActivity({ ...activity(), roles: [
    { ...activity().roles[0], role: 'market-research', status: 'completed' },
    { ...activity().roles[0], role: 'risk-critic', status: 'failed' },
    { ...activity().roles[0], role: 'ceo', status: 'idle' },
  ] }, now));
  const before = state.getCharacters().map((character) => ({ id: character.id, x: character.x, y: character.y }));
  for (let index = 0; index < 1_200; index++) animateLiveOfficeWork(state, 0.25);
  assert.equal(state.getCharacters().length, 8);
  for (const character of state.getCharacters()) {
    assert.equal(character.isActive, false);
    assert.equal(character.state, CharacterState.IDLE);
    assert.equal(character.frame, 0);
    assert.deepEqual(character.path, []);
  }
  assert.deepEqual(state.getCharacters().map((character) => ({ id: character.id, x: character.x, y: character.y })), before);
});

test('typing frames advance only real working characters and stop on completion', () => {
  const state = office();
  applyLiveOfficeActivity(state, readLiveOfficeActivity(activity(), now));
  const character = state.getCharacters()[0];
  animateLiveOfficeWork(state, 0.3);
  assert.equal(character.frame, 1);
  applyLiveOfficeActivity(state, readLiveOfficeActivity({ ...activity(), roles: [{ ...activity().roles[0], status: 'completed' }] }, now));
  animateLiveOfficeWork(state, 0.3);
  assert.equal(character.isActive, false);
  assert.equal(character.frame, 0);
  assert.equal(character.frameTimer, 0);
});

test('known roles keep stable IDs and seats across sparse snapshots and account changes', () => {
  const state = office();
  const allRoles = { ...activity(), roles: LIVE_ACTIVITY_ROLES.map((role) => ({ ...activity().roles[0], role, status: 'completed' })) };
  applyLiveOfficeActivity(state, readLiveOfficeActivity(allRoles, now));
  assert.deepEqual(state.getCharacters().map((character) => character.id).sort((a, b) => a - b), [1, 2, 3, 4, 5, 6, 7, 8]);
  const finalSeat = state.characters.get(8)?.seatId;
  applyLiveOfficeActivity(state, readLiveOfficeActivity({ ...allRoles, accountContext: { mode: 1, accountId: 'fedcba9876543210fedcba9876543210' }, roles: [allRoles.roles[7]] }, now));
  assert.deepEqual(state.getCharacters().map((character) => character.id).sort((a, b) => a - b), [1, 2, 3, 4, 5, 6, 7, 8]);
  assert.equal(state.characters.get(8)?.seatId, finalSeat);
  assert.equal(state.characters.get(8)?.isActive, false);
});

test('the default artwork seats roles at chairs, then benches, then sofas without overlap', () => {
  const assetsDir = fileURLToPath(new URL('../public/assets/', import.meta.url));
  const defaultLayout = buildAssetIndex(assetsDir).defaultLayout;
  assert.ok(defaultLayout);
  const catalog = buildFurnitureCatalog(assetsDir);
  buildDynamicCatalog({ catalog, sprites: decodeAllFurniture(assetsDir, catalog) });
  const source = readFileSync(`${assetsDir}/${defaultLayout}`, 'utf8');
  const state = new OfficeState(migrateLayoutColors(deserializeLayout(source) as OfficeLayout));
  assert.ok(state.seats.size >= LIVE_ACTIVITY_ROLES.length);
  applyLiveOfficeActivity(state, readLiveOfficeActivity({ ...activity(), roles: LIVE_ACTIVITY_ROLES.map((role) => ({ ...activity().roles[0], role, status: 'completed' })) }, now));
  const characters = state.getCharacters();
  assert.equal(characters.length, 8);
  assert.equal(new Set(characters.map((character) => character.seatId)).size, 8);
  assert.equal(new Set(characters.map((character) => `${character.tileCol},${character.tileRow}`)).size, 8);
  const seatFurniture = characters.map((character) => state.layout.furniture.find((item) => character.seatId === item.uid || character.seatId?.startsWith(`${item.uid}:`)));
  const seatTypes = seatFurniture.map((item) => item?.type);
  assert.ok(seatTypes.slice(0, 4).every((type) => type?.startsWith('WOODEN_CHAIR')));
  assert.ok(seatTypes.slice(4, 6).every((type) => type === 'CUSHIONED_BENCH'));
  assert.ok(seatTypes.slice(6).every((type) => type?.startsWith('SOFA')));
  assert.equal(new Set(seatFurniture.slice(6).map((item) => item?.uid)).size, 2);
  for (const character of characters) {
    const seat = state.seats.get(character.seatId!);
    assert.ok(seat);
    assert.equal(character.tileCol, seat.seatCol);
    assert.equal(character.tileRow, seat.seatRow);
    assert.equal(character.isActive, false);
    assert.deepEqual(character.path, []);
  }
  const positions = characters.map((character) => [character.x, character.y]);
  applyLiveOfficeActivity(state, readLiveOfficeActivity({ ...activity(), roles: LIVE_ACTIVITY_ROLES.map((role, index) => ({ ...activity().roles[0], role, status: index === 0 ? 'working' : 'completed' })) }, now));
  animateLiveOfficeWork(state, 0.3);
  assert.equal(state.characters.get(1)?.isActive, true);
  assert.equal(state.characters.get(1)?.frame, 1);
  assert.equal(state.getCharacters().filter((character) => character.isActive).length, 1);
  assert.deepEqual(state.getCharacters().map((character) => [character.x, character.y]), positions);
});
