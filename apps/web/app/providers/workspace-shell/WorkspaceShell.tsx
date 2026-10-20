'use client';

import { Component } from 'react';
import AppShell from '../../../shared/layout/AppShell';
import { TeamChatWidget } from '../../../features/team/components/team-chat-widget/TeamChatWidget';
import type { WorkspaceShellProps } from './WorkspaceShellProps';
import type { WorkspaceShellState } from './WorkspaceShellState';

export class WorkspaceShell extends Component<
  WorkspaceShellProps,
  WorkspaceShellState
> {
  public override state: WorkspaceShellState = { chatOpen: false };

  public override componentDidMount(): void {
    window.addEventListener('popstate', this.historyChanged);
    this.historyChanged();
  }

  public override componentWillUnmount(): void {
    window.removeEventListener('popstate', this.historyChanged);
  }

  private readonly historyChanged = (): void => {
    this.setState({ chatOpen: this.hasChatEntry() });
  };

  private hasChatEntry(): boolean {
    const state: unknown = window.history.state;

    return (
      typeof state === 'object' &&
      state !== null &&
      'quantaraTeamChat' in state &&
      state.quantaraTeamChat === true
    );
  }

  private readonly openChat = (): void => {
    if (!this.state.chatOpen) {
      const state: unknown = window.history.state;
      window.history.pushState(
        {
          ...(typeof state === 'object' && state !== null ? state : {}),
          quantaraTeamChat: true,
        },
        '',
        window.location.href,
      );
      this.setState({ chatOpen: true });
    }
  };

  private readonly closeChat = (): void => {
    this.setState({ chatOpen: false });
    if (this.hasChatEntry()) {
      window.history.back();
    }
  };

  public override render() {
    return (
      <>
        <AppShell
          onOpenTeamChat={this.openChat}
          teamChatOpen={this.state.chatOpen}
        >
          {this.props.children}
        </AppShell>
        <TeamChatWidget
          open={this.state.chatOpen}
          onClose={this.closeChat}
        />
      </>
    );
  }
}
