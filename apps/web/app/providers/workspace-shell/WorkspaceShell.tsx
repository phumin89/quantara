'use client';

import { Component, type ContextType } from 'react';
import AppShell from '../../../shared/layout/AppShell';
import { TeamChatSidebar } from '../../../features/team/components/team-chat-sidebar/TeamChatSidebar';
import type { WorkspaceShellProps } from './WorkspaceShellProps';
import type { WorkspaceShellState } from './WorkspaceShellState';
import { TeamActivityContext } from '../../../features/team/context/TeamActivityContext';

export class WorkspaceShell extends Component<
  WorkspaceShellProps,
  WorkspaceShellState
> {
  public static override contextType = TeamActivityContext.value;

  declare public context: ContextType<typeof TeamActivityContext.value>;

  public override state: WorkspaceShellState = {
    chatExpanded: false,
  };

  private readonly toggleChat = (): void => {
    this.setState((state) => ({ chatExpanded: !state.chatExpanded }));
  };

  public override render() {
    return (
      <AppShell
        accountContext={TeamActivityContext.require(this.context).accountKey}
        sidebar={
          <TeamChatSidebar
            expanded={this.state.chatExpanded}
            onToggle={this.toggleChat}
          />
        }
      >
        {this.props.children}
      </AppShell>
    );
  }
}
