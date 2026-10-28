import type { ReactNode } from 'react';

export interface AppShellProps {
  children: ReactNode;
  readonly accountContext?: string | null;
  readonly sidebar?: ReactNode;
}
