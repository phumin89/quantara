/**
 * World coordinates → screen coordinates, in one place.
 *
 * The office is drawn by centering the map in the canvas and then applying the
 * pan, both snapped to whole device pixels so sprites stay on the pixel grid.
 * That formula was reproduced in the renderer and in each DOM overlay that
 * floats something above a character; a copy that rounds differently puts the
 * overlay a pixel off the sprite it is labelling, which is invisible in review
 * and obvious on screen.
 *
 * Deliberately free of DOM access — `dpr` is passed in, not read from
 * `window`. Reading the environment belongs at the component boundary; a state
 * or math module that reaches for `window` drags the DOM into every module
 * graph that imports it.
 */

import { type OfficeLayout, TILE_SIZE, TileType } from './types.js';

/** Frame the occupied office, excluding unused void tiles in saved layouts. */
export function companyCamera(layout: OfficeLayout, width: number, height: number, dpr: number) {
  let left = layout.cols, top = layout.rows, right = -1, bottom = -1;
  layout.tiles.forEach((tile, index) => {
    if (tile === TileType.VOID) return;
    const col = index % layout.cols, row = Math.floor(index / layout.cols);
    left = Math.min(left, col); right = Math.max(right, col);
    top = Math.min(top, row); bottom = Math.max(bottom, row);
  });
  if (right < 0) return { zoom: 1, pan: { x: 0, y: 0 } };
  const zoom = Math.max(1, Math.min(10, Math.floor(Math.min(
    Math.max(1, width - 24) * dpr / ((right - left + 1) * TILE_SIZE),
    Math.max(1, height - 24) * dpr / ((bottom - top + 1) * TILE_SIZE),
  ))));
  return { zoom, pan: {
    x: (layout.cols / 2 - (left + right + 1) / 2) * TILE_SIZE * zoom,
    y: (layout.rows / 2 - (top + bottom + 1) / 2) * TILE_SIZE * zoom,
  } };
}

/** Device-pixel offset of the map's top-left corner inside the canvas.
 *  This is the renderer's own frame of reference — overlays go through
 *  {@link overlayProjection} instead of calling this directly. */
export function mapOffset(
  canvasWidth: number,
  canvasHeight: number,
  cols: number,
  rows: number,
  zoom: number,
  panX: number,
  panY: number,
): { offsetX: number; offsetY: number } {
  const mapW = cols * TILE_SIZE * zoom;
  const mapH = rows * TILE_SIZE * zoom;
  return {
    offsetX: Math.floor((canvasWidth - mapW) / 2) + Math.round(panX),
    offsetY: Math.floor((canvasHeight - mapH) / 2) + Math.round(panY),
  };
}

/** Projects world points into CSS pixels within the overlay container that
 *  sits on top of the canvas. */
export interface OverlayProjection {
  toScreenX(worldX: number): number;
  toScreenY(worldY: number): number;
  /** Container size in world units — what the viewport currently covers.
   *  Used to cap overlay offsets against the visible area. */
  readonly viewportWorldWidth: number;
  readonly viewportWorldHeight: number;
  /** CSS px → world units, for sizing overlay geometry in world terms. */
  toWorldLength(cssPx: number): number;
}

export function overlayProjection(
  layout: { cols: number; rows: number },
  containerRect: { width: number; height: number },
  zoom: number,
  pan: { x: number; y: number },
  dpr: number,
): OverlayProjection {
  const canvasW = Math.round(containerRect.width * dpr);
  const canvasH = Math.round(containerRect.height * dpr);
  const { offsetX, offsetY } = mapOffset(
    canvasW,
    canvasH,
    layout.cols,
    layout.rows,
    zoom,
    pan.x,
    pan.y,
  );
  return {
    toScreenX: (worldX) => (offsetX + worldX * zoom) / dpr,
    toScreenY: (worldY) => (offsetY + worldY * zoom) / dpr,
    viewportWorldWidth: canvasW / zoom,
    viewportWorldHeight: canvasH / zoom,
    toWorldLength: (cssPx) => (cssPx * dpr) / zoom,
  };
}
