export interface PixelOfficeBridge {
  sync(agents: ReadonlyArray<{ id: number; active: boolean }>): void;
  select(id: number | null): void;
  advance(seconds: number): void;
  draw(): void;
  hitTest(x: number, y: number): number | null;
}
