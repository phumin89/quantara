import type { PixelOfficeBridge } from './PixelOfficeBridge';

export interface PixelOfficeModule {
  PixelAgentsOffice: {
    create(canvas: HTMLCanvasElement): Promise<PixelOfficeBridge>;
  };
}
