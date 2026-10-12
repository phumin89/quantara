import type { ConnectionState } from '../contracts/ConnectionState';

export interface ConnectionStateAppearance {
  readonly label: ConnectionState;
  readonly color: string;
  readonly background: string;
}
