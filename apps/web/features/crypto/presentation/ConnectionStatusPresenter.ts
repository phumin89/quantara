import { ConnectionState } from '../contracts/ConnectionState';
import type { ConnectionStateAppearance } from './ConnectionStateAppearance';

export class ConnectionStatusPresenter {
  public constructor(
    private readonly error: string | null,
    private readonly syncing: boolean,
    private readonly updatedAt: string | null,
  ) {}

  public get state(): ConnectionState {
    if (this.error) {
      return ConnectionState.Error;
    }

    if (this.syncing) {
      return ConnectionState.Syncing;
    }

    if (this.updatedAt) {
      return ConnectionState.Connected;
    }

    return ConnectionState.Checking;
  }

  public get appearance(): ConnectionStateAppearance {
    const label = this.state;

    switch (label) {
      case ConnectionState.Error:
        return { label, color: '#dc2626', background: '#fef2f2' };
      case ConnectionState.Syncing:
        return { label, color: '#b45309', background: '#fffbeb' };
      case ConnectionState.Connected:
        return { label, color: '#15803d', background: '#f0fdf4' };
      case ConnectionState.Checking:
        return { label, color: '#525252', background: '#f3f3f3' };
    }
  }
}
