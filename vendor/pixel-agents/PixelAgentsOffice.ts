import { OfficeState } from './upstream/webview-ui/src/office/engine/officeState.js';
import { renderFrame } from './upstream/webview-ui/src/office/engine/renderer.js';
import { setCharacterTemplates } from './upstream/webview-ui/src/office/sprites/spriteData.js';
import { buildDynamicCatalog } from './upstream/webview-ui/src/office/layout/furnitureCatalog.js';
import {
  CharacterState,
  Direction,
  TileType,
  type SpriteData,
} from './upstream/webview-ui/src/office/types.js';

/** Quantara's renderer-only seam. No upstream transports, hooks, terminals or session discovery. */
export class PixelAgentsOffice {
  private static loading: Promise<void> | null = null;
  private readonly office: OfficeState;
  private readonly labels = [
    'CEO',
    'Analyst',
    'Reviewer',
    'Intraday',
    'Swing',
    'News',
    'Positions',
    'Quant',
  ];
  private offset = { offsetX: 0, offsetY: 0 };
  private zoom = 1;

  private constructor(private readonly canvas: HTMLCanvasElement) {
    const cols = 24;
    const rows = 16;
    const furniture = this.labels.flatMap((_, index) => {
      const col = 3 + (index % 4) * 5;
      const row = index < 4 ? 4 : 10;
      return [
        { uid: `seat-${index}`, type: 'chair', col, row },
        { uid: `desk-${index}`, type: 'desk', col, row: row + 1 },
      ];
    });
    this.office = new OfficeState({
      version: 1,
      cols,
      rows,
      tiles: Array.from({ length: cols * rows }, (_, index) =>
        index < cols ||
        index >= cols * (rows - 1) ||
        index % cols === 0 ||
        index % cols === cols - 1
          ? TileType.WALL
          : TileType.FLOOR_1,
      ),
      furniture,
    });
    this.labels.forEach((_, index) => {
      this.office.addAgent(
        index + 1,
        index % 6,
        index >= 6 ? 35 : 0,
        `seat-${index}`,
        true,
      );
      const character = this.office.characters.get(index + 1);
      if (character) {
        character.seatTimer = 1e9;
        character.wanderTimer = 1e9;
      }
    });
  }

  public static async create(
    canvas: HTMLCanvasElement,
  ): Promise<PixelAgentsOffice> {
    this.loading ??= this.initialize();
    try {
      await this.loading;
    } catch (error) {
      this.loading = null;
      throw error;
    }
    return new PixelAgentsOffice(canvas);
  }

  public sync(agents: ReadonlyArray<{ id: number; active: boolean }>): void {
    for (const agent of agents) {
      this.office.setAgentActive(agent.id, agent.active);
      // No tool name is invented from a research step; only actual running state animates.
      this.office.setAgentTool(agent.id, null);
      const character = this.office.characters.get(agent.id);
      if (character) {
        character.state = agent.active
          ? CharacterState.TYPE
          : CharacterState.IDLE;
        character.seatTimer = 1e9;
        character.wanderTimer = 1e9;
        character.path = [];
      }
    }
  }

  public select(id: number | null): void {
    this.office.selectedAgentId = id;
  }

  public advance(seconds: number): void {
    this.office.update(Math.min(seconds, 0.1));
  }

  public draw(): void {
    const context = this.canvas.getContext('2d');
    if (!context) return;
    const { width, height } = this.canvas;
    this.zoom = Math.min(width / 400, height / 272);
    context.imageSmoothingEnabled = false;
    this.offset = renderFrame(
      context,
      width,
      height,
      this.office.tileMap,
      this.office.furniture,
      [...this.office.characters.values()],
      this.zoom,
      0,
      0,
      {
        selectedAgentId: this.office.selectedAgentId,
        hoveredAgentId: null,
        hoveredTile: null,
        seats: this.office.seats,
        characters: this.office.characters,
      },
    );
    context.fillStyle = '#141414';
    context.textAlign = 'center';
    context.font = `${Math.max(11, this.zoom * 5)}px system-ui, sans-serif`;
    this.labels.forEach((label, index) => {
      const character = this.office.characters.get(index + 1);
      if (character)
        context.fillText(
          label,
          this.offset.offsetX + character.x * this.zoom,
          this.offset.offsetY + (character.y + 32) * this.zoom,
        );
    });
  }

  public hitTest(x: number, y: number): number | null {
    for (const character of this.office.characters.values()) {
      const cx = this.offset.offsetX + character.x * this.zoom;
      const cy = this.offset.offsetY + character.y * this.zoom;
      if (
        Math.abs(x - cx) <= 16 * this.zoom &&
        y >= cy - 36 * this.zoom &&
        y <= cy + 14 * this.zoom
      )
        return character.id;
    }
    return null;
  }

  private static async initialize(): Promise<void> {
    setCharacterTemplates(
      await Promise.all(
        Array.from({ length: 6 }, (_, index) => this.loadCharacter(index)),
      ),
    );
    const desk = this.sprite(32, 24, (x, y) =>
      y < 12
        ? x > 7 && x < 24 && y > 1
          ? y < 9
            ? '#354353'
            : '#141414'
          : ''
        : y < 20
          ? '#c4b8a7'
          : x < 3 || x > 28
            ? '#6e6253'
            : '',
    );
    const chair = this.sprite(16, 16, (x, y) =>
      x > 3 && x < 13 && y > 1 && y < 14 ? '#64716c' : '',
    );
    buildDynamicCatalog({
      catalog: [
        {
          id: 'desk',
          label: 'Desk',
          category: 'desks',
          width: 32,
          height: 24,
          footprintW: 2,
          footprintH: 1,
          isDesk: true,
        },
        {
          id: 'chair',
          label: 'Chair',
          category: 'chairs',
          width: 16,
          height: 16,
          footprintW: 1,
          footprintH: 1,
          isDesk: false,
          orientation: 'front',
        },
      ],
      sprites: { desk, chair },
    });
  }

  private static sprite(
    width: number,
    height: number,
    pixel: (x: number, y: number) => string,
  ): SpriteData {
    return Array.from({ length: height }, (_, y) =>
      Array.from({ length: width }, (_, x) => pixel(x, y)),
    );
  }

  private static async loadCharacter(index: number) {
    const image = new Image();
    image.src = `/vendor/pixel-agents/char_${index}.png`;
    await image.decode();
    if (image.naturalWidth !== 112 || image.naturalHeight !== 96)
      throw new Error('Invalid character atlas.');
    const canvas = document.createElement('canvas');
    canvas.width = 112;
    canvas.height = 96;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Canvas is unavailable.');
    context.drawImage(image, 0, 0);
    const frame = (direction: number, column: number): SpriteData => {
      const rgba = context.getImageData(
        column * 16,
        direction * 32,
        16,
        32,
      ).data;
      return this.sprite(16, 32, (x, y) => {
        const start = (y * 16 + x) * 4;
        if (rgba[start + 3] === 0) return '';
        return (
          '#' +
          [...rgba.slice(start, start + 4)]
            .map((value) => value.toString(16).padStart(2, '0'))
            .join('')
        );
      });
    };
    const frames = (direction: number) =>
      Array.from({ length: 7 }, (_, column) => frame(direction, column));
    return { down: frames(Direction.DOWN), up: frames(1), right: frames(2) };
  }
}
