import type { OfficeAgent } from '../../contracts/OfficeAgent';

export interface OfficeCanvasProps {
  readonly agents: readonly OfficeAgent[];
  readonly selectedId: number | null;
  readonly paused: boolean;
  readonly onSelect: (id: number) => void;
}
