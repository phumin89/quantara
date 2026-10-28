import { ConnectionState } from '../contracts/ConnectionState';
import type { ConnectionStateAppearance } from './ConnectionStateAppearance';

export class ConnectionStatusPresenter {
  public static readonly maximumSourceAgeMs = 120000;

  public constructor(
    private readonly error: string | null,
    private readonly syncing: boolean,
    private readonly updatedAt: string | null,
    private readonly now: number = Date.now(),
    private readonly loaded: boolean = false,
  ) {}

  public get state(): ConnectionState {
    if (this.error) {
      return ConnectionState.Error;
    }

    if (this.syncing) {
      return ConnectionState.Syncing;
    }

    if (this.updatedAt === null) {
      return this.loaded
        ? ConnectionState.Unavailable
        : ConnectionState.Checking;
    }

    const at = Date.parse(this.updatedAt);
    if (!Number.isFinite(at) || at - this.now > 30000) {
      return ConnectionState.Unavailable;
    }

    return this.now - at > ConnectionStatusPresenter.maximumSourceAgeMs
      ? ConnectionState.Stale
      : ConnectionState.Connected;
  }

  public get appearance(): ConnectionStateAppearance {
    const label = this.state;

    switch (label) {
      case ConnectionState.Error:
        return {
          label,
          color: 'var(--color-error)',
          background: 'var(--surface-error)',
        };
      case ConnectionState.Syncing:
        return {
          label,
          color: 'var(--color-warning)',
          background: 'var(--surface-warning)',
        };
      case ConnectionState.Connected:
        return {
          label,
          color: 'var(--color-success)',
          background: 'var(--surface-success)',
        };
      case ConnectionState.Checking:
        return {
          label,
          color: 'var(--color-muted)',
          background: 'var(--surface-soft)',
        };
      case ConnectionState.Stale:
      case ConnectionState.Unavailable:
        return {
          label,
          color: 'var(--color-warning)',
          background: 'var(--surface-warning)',
        };
    }
  }
}
