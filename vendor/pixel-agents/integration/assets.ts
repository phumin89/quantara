import { readFileSync } from 'node:fs';
import path from 'node:path';
import { buildFurnitureCatalog } from '../upstream/core/src/assets/build.ts';
import { decodeAllCharacters, decodeAllFloors, decodeAllWalls, decodeAllCarpets, decodeAllFurniture } from '../upstream/core/src/assets/loader.ts';

export function loadOfficeAssets(root: string) {
  const catalog = buildFurnitureCatalog(root);
  return {
    characters: decodeAllCharacters(root), floors: decodeAllFloors(root),
    walls: decodeAllWalls(root), carpets: decodeAllCarpets(root), catalog,
    furniture: decodeAllFurniture(root, catalog),
    layout: JSON.parse(readFileSync(path.join(root, 'default-layout-1.json'), 'utf8')),
  };
}
