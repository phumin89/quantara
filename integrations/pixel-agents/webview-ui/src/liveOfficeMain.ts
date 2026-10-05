import { loadOfficeAssets } from './browserMock.js';
import { companyCamera } from './office/projection.js';
import { setFloorSprites } from './office/floorTiles.js';
import { setWallSprites } from './office/wallTiles.js';
import { setCarpetSprites } from './office/sprites/carpetTiles.js';
import { setCharacterTemplates } from './office/sprites/spriteData.js';
import { buildDynamicCatalog } from './office/layout/furnitureCatalog.js';
import { deserializeLayout, migrateLayoutColors } from './office/layout/layoutSerializer.js';
import { OfficeState } from './office/engine/officeState.js';
import { renderFrame } from './office/engine/renderer.js';
import type { OfficeLayout } from './office/types.js';

const root = document.querySelector<HTMLElement>('#live-office') as HTMLElement | null;
if (!root) throw new Error('Live office root is missing');
const officeRoot: HTMLElement = root;

const canvas = document.createElement('canvas');
canvas.style.cssText = 'display:block;width:100%;height:100%;image-rendering:pixelated';
canvas.setAttribute('aria-label', 'สำนักงาน Pixel ของ Quantara');
canvas.setAttribute('role', 'img');
root.append(canvas);
let observer: ResizeObserver | null = null;

async function start(): Promise<void> {
  try {
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
    const resize = () => {
      const rect = officeRoot.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.max(1, Math.round(rect.width * dpr));
      canvas.height = Math.max(1, Math.round(rect.height * dpr));
      canvas.style.imageRendering = 'pixelated';
      const ctx = canvas.getContext('2d');
      if (!ctx) throw new Error('Canvas 2D context unavailable');
      ctx.imageSmoothingEnabled = false;
      const camera = companyCamera(state.layout, rect.width, rect.height, dpr);
      renderFrame(ctx, canvas.width, canvas.height, state.tileMap, state.furniture, [], camera.zoom, camera.pan.x, camera.pan.y, undefined, undefined, state.layout.tileColors, state.layout.cols, state.layout.rows, state.layout.carpetTiles, state.layout.areas, state.layout.areaTiles, false, null, []);
    };
    observer = new ResizeObserver(() => {
      try { resize(); } catch (error) { console.error('[LiveOffice] redraw failed', error); }
    });
    observer.observe(officeRoot);
    resize();
  } catch (error) {
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
