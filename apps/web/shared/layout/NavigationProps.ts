export interface NavigationProps {
  onNavigate?: () => void;
  readonly onOpenTeamChat?: (() => void) | undefined;
  readonly teamChatOpen?: boolean | undefined;
}
