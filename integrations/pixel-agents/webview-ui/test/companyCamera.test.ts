import { describe, expect, it } from 'vitest';

import { companyCamera, mapOffset } from '../src/office/projection.js';
import { type OfficeLayout, TILE_SIZE, TileType } from '../src/office/types.js';

describe('company office camera', () => {
  it.each([1, 2])('fits the occupied office at mobile DPR %s without saved-layout mutation', (dpr) => {
    const layout: OfficeLayout = { version: 1, cols: 24, rows: 24, furniture: [], tiles: Array(24 * 24).fill(TileType.VOID) };
    for (let row = 10; row < 21; row++) for (let col = 2; col < 22; col++) layout.tiles[row * 24 + col] = TileType.FLOOR_1;
    const before = structuredClone(layout);
    const camera = companyCamera(layout, 360, 360, dpr);
    const offset = mapOffset(360 * dpr, 360 * dpr, 24, 24, camera.zoom, camera.pan.x, camera.pan.y);
    expect(offset.offsetX + 2 * TILE_SIZE * camera.zoom).toBeGreaterThanOrEqual(0);
    expect(offset.offsetY + 10 * TILE_SIZE * camera.zoom).toBeGreaterThanOrEqual(0);
    expect(offset.offsetX + 22 * TILE_SIZE * camera.zoom).toBeLessThanOrEqual(360 * dpr);
    expect(offset.offsetY + 21 * TILE_SIZE * camera.zoom).toBeLessThanOrEqual(360 * dpr);
    expect(layout).toEqual(before);
  });
  it('has a finite safe camera for an empty office', () => {
    expect(companyCamera({ version: 1, cols: 1, rows: 1, furniture: [], tiles: [TileType.VOID] }, 0, 0, 1))
      .toEqual({ zoom: 1, pan: { x: 0, y: 0 } });
  });
});
