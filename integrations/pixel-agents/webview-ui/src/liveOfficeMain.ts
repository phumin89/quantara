import { loadOfficeAssets } from './browserMock.js';
import type { LiveOfficeActivity } from './liveOfficeActivity.js';
import {
  animateLiveOfficeWork,
  applyLiveOfficeActivity,
  isLiveOfficeActivityFresh,
  LIVE_ACTIVITY_MAX_AGE_MS,
  readLiveOfficeMessage,
} from './liveOfficeActivity.js';
import { OfficeState } from './office/engine/officeState.js';
import { renderFrame } from './office/engine/renderer.js';
import { setFloorSprites } from './office/floorTiles.js';
import { buildDynamicCatalog } from './office/layout/furnitureCatalog.js';
import { deserializeLayout, migrateLayoutColors } from './office/layout/layoutSerializer.js';
import { companyCamera } from './office/projection.js';
import { setCarpetSprites } from './office/sprites/carpetTiles.js';
import { setCharacterTemplates } from './office/sprites/spriteData.js';
import type { OfficeLayout } from './office/types.js';
import { setWallSprites } from './office/wallTiles.js';

const root = document.querySelector<HTMLElement>('#live-office') as HTMLElement | null;
if (!root) throw new Error('Live office root is missing');
const officeRoot: HTMLElement = root;

const canvas = document.createElement('canvas');
canvas.style.cssText = 'display:block;width:100%;height:100%;image-rendering:pixelated';
canvas.setAttribute('aria-label', 'สำนักงาน Pixel ของ Quantara');
canvas.setAttribute('role', 'img');
root.append(canvas);
let observer: ResizeObserver | null = null;
let activity: LiveOfficeActivity | null = null;
let updateActivity: (() => void) | null = null;
let disposeViewer: (() => void) | null = null;

window.addEventListener('message', (event: MessageEvent) => {
  const message = readLiveOfficeMessage(event, window.parent, window, Date.now());
  if (!message) return;
  activity = message.activity;
  updateActivity?.();
});
window.addEventListener('pagehide', () => disposeViewer?.());

async function start(): Promise<void> {
  try {
    disposeViewer?.(); disposeViewer = null;
    observer?.disconnect();
    officeRoot.replaceChildren(canvas);
    const assets = await loadOfficeAssets();
    setCharacterTemplates(assets.characters);
    setFloorSprites(assets.floorSprites);
    setWallSprites(assets.wallSets);
    setCarpetSprites(assets.carpetSets);
    buildDynamicCatalog({ catalog: assets.furnitureCatalog, sprites: assets.furnitureSprites });
    if (!assets.layout) throw new Error('Default office layout is unavailable');
    const layout = migrateLayoutColors(deserializeLayout(JSON.stringify(assets.layout)) as OfficeLayout);
    const state = new OfficeState(layout);
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let frame: number | null = null;
    let expiry: number | null = null;
    let previousTime: number | null = null;
    const draw = () => {
      const rect = officeRoot.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      const ctx = canvas.getContext('2d');
      if (!ctx) throw new Error('Canvas 2D context unavailable');
      ctx.imageSmoothingEnabled = false;
      const camera = companyCamera(state.layout, rect.width, rect.height, dpr);
      renderFrame(ctx, canvas.width, canvas.height, state.tileMap, state.furniture, state.getCharacters(), camera.zoom, camera.pan.x, camera.pan.y, undefined, undefined, state.layout.tileColors, state.layout.cols, state.layout.rows, state.layout.carpetTiles, state.layout.areas, state.layout.areaTiles, false, null, []);
    };
    const resize = () => {
      const rect = officeRoot.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.max(1, Math.round(rect.width * dpr));
      canvas.height = Math.max(1, Math.round(rect.height * dpr));
      canvas.style.imageRendering = 'pixelated';
      draw();
    };
    const stopAnimation = () => {
      if (frame !== null) window.cancelAnimationFrame(frame);
      frame = null;
      previousTime = null;
    };
    const hasWork = () => state.getCharacters().some((character) => character.isActive);
    const scheduleAnimation = () => {
      if (frame === null && !reducedMotion.matches && hasWork()) frame = window.requestAnimationFrame(tick);
    };
    const tick = (time: number) => {
      frame = null;
      if (!activity || !isLiveOfficeActivityFresh(activity, Date.now())) {
        activity = null;
        refreshActivity();
        return;
      }
      const elapsed = previousTime === null ? 0 : Math.min(0.1, Math.max(0, (time - previousTime) / 1_000));
      previousTime = time;
      animateLiveOfficeWork(state, elapsed);
      draw();
      scheduleAnimation();
    };
    const refreshActivity = () => {
      if (expiry !== null) window.clearTimeout(expiry);
      expiry = null;
      if (activity && !isLiveOfficeActivityFresh(activity, Date.now())) activity = null;
      applyLiveOfficeActivity(state, activity);
      if (activity?.roles.length) {
        expiry = window.setTimeout(() => {
          activity = null;
          refreshActivity();
        }, Math.max(1, Date.parse(activity.observedAt) + LIVE_ACTIVITY_MAX_AGE_MS - Date.now()));
      }
      if (!hasWork() || reducedMotion.matches) stopAnimation();
      else scheduleAnimation();
      draw();
    };
    const motionChanged = () => {
      stopAnimation();
      for (const character of state.getCharacters()) {
        character.frame = 0;
        character.frameTimer = 0;
      }
      draw();
      scheduleAnimation();
    };
    reducedMotion.addEventListener('change', motionChanged);
    updateActivity = refreshActivity;
    disposeViewer = () => {
      stopAnimation();
      if (expiry !== null) window.clearTimeout(expiry);
      reducedMotion.removeEventListener('change', motionChanged);
      updateActivity = null;
    };
    observer = new ResizeObserver(() => {
      try { resize(); } catch (error) { console.error('[LiveOffice] redraw failed', error); }
    });
    observer.observe(officeRoot);
    resize();
    refreshActivity();
  } catch (error) {
    disposeViewer?.(); disposeViewer = null;
    observer?.disconnect(); observer = null;
    officeRoot.replaceChildren();
    const message = document.createElement('p');
    message.textContent = 'โหลดภาพสำนักงานไม่สำเร็จ กรุณาลองใหม่';
    officeRoot.append(message);
    const retry = document.createElement('button');
    retry.type = 'button'; retry.textContent = 'ลองใหม่'; retry.onclick = () => void start();
    officeRoot.append(retry);
    console.error('[LiveOffice]', error);
  }
}

void start();
