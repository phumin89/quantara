'use client';

import { Component } from 'react';
import MenuIcon from '@mui/icons-material/Menu';
import ForumOutlinedIcon from '@mui/icons-material/ForumOutlined';
import { Box, Button, Drawer, IconButton, Stack } from '@mui/material';
import Navigation from './Navigation';
import type { AppShellProps } from './AppShellProps';
import type { AppShellState } from './AppShellState';

export default class AppShell extends Component<AppShellProps, AppShellState> {
  private static readonly drawerWidth = 240;

  public override state: AppShellState = { isMobile: false, open: false };

  private media: MediaQueryList | null = null;

  private readonly updateViewport = (): void => {
    this.setState({ isMobile: this.media?.matches ?? false, open: false });
  };

  public override componentDidMount(): void {
    this.media = window.matchMedia('(max-width: 767.98px)');
    this.media.addEventListener('change', this.updateViewport);
    this.updateViewport();
  }

  public override componentWillUnmount(): void {
    this.media?.removeEventListener('change', this.updateViewport);
  }

  public override render() {
    const { children } = this.props;
    const { isMobile, open } = this.state;
    const navigation = (
      <Navigation
        onOpenTeamChat={isMobile ? undefined : this.props.onOpenTeamChat}
        teamChatOpen={this.props.teamChatOpen}
        onNavigate={() => {
          this.setState({ open: false });
        }}
      />
    );

    return (
      <Box sx={{ display: 'flex', minHeight: '100dvh', width: '100%' }}>
        {isMobile ? (
          <Drawer
            open={open}
            onClose={() => {
              this.setState({ open: false });
            }}
            ModalProps={{ keepMounted: true }}
            aria-label="Navigation menu"
            sx={{
              '& .MuiDrawer-paper': {
                width: AppShell.drawerWidth,
                boxSizing: 'border-box',
              },
            }}
          >
            {navigation}
          </Drawer>
        ) : (
          <Drawer
            variant="permanent"
            open
            sx={{
              width: AppShell.drawerWidth,
              flexShrink: 0,
              '& .MuiDrawer-paper': {
                width: AppShell.drawerWidth,
                boxSizing: 'border-box',
              },
            }}
          >
            {navigation}
          </Drawer>
        )}
        <Box
          component="main"
          sx={{ flexGrow: 1, minWidth: 0, p: { xs: 3, md: 6 } }}
        >
          {isMobile && (
            <Stack
              direction="row"
              sx={{
                justifyContent: 'space-between',
                alignItems: 'center',
                mb: 4,
              }}
            >
              <IconButton
                onClick={() => {
                  this.setState({ open: true });
                }}
                aria-label="Open menu"
                aria-expanded={open}
                sx={{ color: 'text.primary', width: 44, height: 44 }}
              >
                <MenuIcon />
              </IconButton>
              {this.props.onOpenTeamChat && (
                <Button
                  onClick={this.props.onOpenTeamChat}
                  startIcon={<ForumOutlinedIcon />}
                  aria-haspopup="dialog"
                  aria-expanded={this.props.teamChatOpen ?? false}
                  aria-controls="team-chat-dialog"
                  sx={{ minHeight: 44 }}
                >
                  Team chat
                </Button>
              )}
            </Stack>
          )}
          {children}
        </Box>
      </Box>
    );
  }
}
