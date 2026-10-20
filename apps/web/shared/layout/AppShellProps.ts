import type { ReactNode } from 'react';

export interface AppShellProps {
  children: ReactNode;
  readonly onOpenTeamChat?: () => void;
  readonly teamChatOpen?: boolean;
}
