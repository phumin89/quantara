'use client';

import { Component, createRef, type ContextType } from 'react';
import {
  Alert,
  Box,
  Button,
  Drawer,
  IconButton,
  Portal,
  Typography,
} from '@mui/material';
import ChatBubbleOutlineRounded from '@mui/icons-material/ChatBubbleOutlineRounded';
import CloseRounded from '@mui/icons-material/CloseRounded';
import RefreshOutlined from '@mui/icons-material/RefreshOutlined';
import { TeamActivityContext } from '../../context/TeamActivityContext';
import DailyBriefConversation from '../daily-brief-conversation/DailyBriefConversation';
import type { TeamChatSidebarProps } from './TeamChatSidebarProps';

export class TeamChatSidebar extends Component<TeamChatSidebarProps> {
  public static override contextType = TeamActivityContext.value;

  declare public context: ContextType<typeof TeamActivityContext.value>;

  private readonly heading = createRef<HTMLHeadingElement>();

  private readonly toggleChat = (): void => {
    this.props.onToggle();
  };

  private readonly closeChat = (): void => {
    if (this.props.expanded) {
      this.props.onToggle();
    }
  };

  public override render() {
    const activity = TeamActivityContext.require(this.context);
    const { expanded } = this.props;
    const stale =
      activity.fetchedAt === null ||
      activity.error !== null ||
      activity.now - activity.fetchedAt > 30000;
    const runs = activity.brief?.runs ?? [];
    const latestRun = runs.reduce(
      (latest, run) =>
        !latest || Date.parse(run.updatedAt) > Date.parse(latest.updatedAt)
          ? run
          : latest,
      runs[0],
    );
    let status = runs.length ? 'Research snapshot' : 'No research snapshot';
    if (activity.loading) {
      status = 'Checking';
    } else if (stale) {
      status = 'Stale / unavailable';
    }

    return (
      <>
        <Portal container={() => document.getElementById('team-chat-toggle')}>
          <Button
            aria-label={expanded ? 'Toggle team chat' : 'Expand team chat'}
            aria-expanded={expanded}
            aria-controls="team-chat-content"
            onClick={this.toggleChat}
            variant={expanded ? 'outlined' : 'contained'}
            startIcon={<ChatBubbleOutlineRounded fontSize="small" />}
            sx={{
              minHeight: 44,
              px: { xs: 1, sm: 1.5 },
              borderRadius: 'var(--radius-control)',
              whiteSpace: 'nowrap',
            }}
          >
            <Box
              component="span"
              sx={{ display: { xs: 'none', sm: 'inline' } }}
            >
              Team chat
            </Box>
            <Box
              component="span"
              sx={{ display: { xs: 'inline', sm: 'none' } }}
            >
              Chat
            </Box>
          </Button>
        </Portal>
        <Drawer
          anchor="right"
          variant="temporary"
          open={expanded}
          onClose={this.closeChat}
          ModalProps={{ keepMounted: true }}
          transitionDuration={{ enter: 320, exit: 200 }}
          elevation={0}
          slotProps={{
            transition: {
              easing: {
                enter: 'cubic-bezier(0.16, 1, 0.3, 1)',
                exit: 'cubic-bezier(0.4, 0, 1, 1)',
              },
              onEntered: () => {
                this.heading.current?.focus({ preventScroll: true });
              },
            },
            backdrop: {
              sx: { bgcolor: 'rgb(0 0 0 / 0.22)' },
            },
            paper: {
              inert: !expanded,
              role: 'dialog',
              'aria-modal': true,
              'aria-labelledby': 'team-chat-heading',
            },
          }}
          sx={{
            '& .MuiDrawer-paper': {
              display: 'flex',
              pointerEvents: expanded ? 'auto' : 'none',
              top: 80,
              right: 16,
              bottom: 'max(16px, env(safe-area-inset-bottom))',
              height: 'auto',
              width: {
                xs: 'calc(100% - 32px)',
                sm: 'var(--chat-sidebar-width)',
                lg: 'var(--chat-sidebar-width)',
              },
              maxWidth: 'calc(100% - 32px)',
              border: '1px solid',
              borderColor: 'var(--border-panel)',
              borderRadius: 'var(--radius-panel)',
              bgcolor: 'var(--surface-raised)',
              backdropFilter: 'blur(18px)',
              boxShadow: 'var(--shadow-overlay)',
              overflow: 'hidden',
              '@media (prefers-reduced-motion: reduce)': {
                transition: 'none !important',
              },
            },
          }}
        >
          <Box
            component="aside"
            aria-label="Team conversation"
            sx={{
              display: 'flex',
              flexDirection: 'column',
              width: '100%',
              height: '100%',
              minHeight: 0,
              minWidth: 0,
              bgcolor: 'transparent',
              borderRadius: 'var(--radius-panel)',
              overflow: 'hidden',
            }}
          >
            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 1.5,
                minHeight: 64,
                flexShrink: 0,
                px: 2,
                py: 1.5,
                borderBottom: '1px solid var(--border-panel)',
              }}
            >
              <Box
                aria-hidden="true"
                sx={{
                  display: 'grid',
                  placeItems: 'center',
                  width: 40,
                  height: 40,
                  flexShrink: 0,
                  borderRadius: 'var(--radius-control)',
                  bgcolor: 'primary.main',
                  color: 'primary.contrastText',
                  fontSize: 18,
                  fontWeight: 800,
                }}
              >
                Q
              </Box>
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography
                  ref={this.heading}
                  component="h2"
                  id="team-chat-heading"
                  tabIndex={-1}
                  sx={{
                    m: 0,
                    fontSize: 19,
                    fontWeight: 600,
                    outlineOffset: -2,
                  }}
                >
                  Team chat
                </Typography>
                <Typography
                  variant="caption"
                  color="text.secondary"
                  sx={{ display: 'block', fontSize: 12 }}
                >
                  Research updates
                </Typography>
              </Box>
              <IconButton
                aria-label="Collapse team chat"
                aria-expanded={expanded}
                aria-controls="team-chat-content"
                onClick={this.closeChat}
                sx={{ width: 44, height: 44, flexShrink: 0 }}
              >
                <CloseRounded fontSize="small" />
              </IconButton>
            </Box>
            <Box
              id="team-chat-content"
              sx={{
                display: 'flex',
                flexDirection: 'column',
                flex: 1,
                minHeight: 0,
              }}
            >
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 1,
                  mx: 2,
                  my: 1.5,
                  px: 1,
                  py: 0.75,
                  borderRadius: 'var(--radius-control)',
                  bgcolor: 'var(--surface-soft)',
                  flexShrink: 0,
                }}
              >
                <Typography
                  variant="caption"
                  color="text.secondary"
                  sx={{ fontSize: 12, fontWeight: 650 }}
                >
                  {status}
                  {latestRun && (
                    <Box
                      component="span"
                      sx={{ display: 'block' }}
                    >
                      Updated{' '}
                      {new Date(latestRun.updatedAt).toLocaleString('en-GB', {
                        timeZone: 'Asia/Bangkok',
                        dateStyle: 'short',
                        timeStyle: 'short',
                      })}{' '}
                      Bangkok
                    </Box>
                  )}
                  <Box
                    component="span"
                    sx={{ display: 'block' }}
                  >
                    Open Trading to view execution and AI strategy status.
                  </Box>
                </Typography>
                <Button
                  href="/crypto"
                  size="small"
                  sx={{ minHeight: 40 }}
                >
                  View live trading
                </Button>
                <IconButton
                  aria-label="Refresh team chat"
                  onClick={() => {
                    void activity.refresh();
                  }}
                  sx={{
                    width: 44,
                    height: 44,
                    flexShrink: 0,
                    borderRadius: 'var(--radius-control)',
                    bgcolor: 'var(--surface-raised)',
                  }}
                >
                  <RefreshOutlined fontSize="small" />
                </IconButton>
              </Box>
              {activity.error && (
                <Alert
                  severity="warning"
                  sx={{
                    mx: 1,
                    mb: 1,
                    maxHeight: '30%',
                    overflowY: 'auto',
                    flexShrink: 1,
                  }}
                  action={
                    <Button
                      color="inherit"
                      onClick={() => {
                        void activity.refresh();
                      }}
                    >
                      Retry
                    </Button>
                  }
                >
                  {!!activity.brief?.runs.length && (
                    <Typography
                      variant="body2"
                      sx={{ mb: 1 }}
                    >
                      Previous verified research is shown. This refresh did not
                      verify the current account context and reports.
                    </Typography>
                  )}
                  {activity.error}
                </Alert>
              )}
              <Box sx={{ flex: 1, minHeight: 0 }}>
                <DailyBriefConversation
                  key={`${String(activity.scopeRevision)}:${activity.date}`}
                  active={expanded}
                  brief={activity.brief}
                  loading={activity.loading}
                  error={activity.error}
                  date={activity.date}
                  embedded
                  preservePosition
                  idPrefix="team-chat-run-"
                />
              </Box>
            </Box>
          </Box>
        </Drawer>
      </>
    );
  }
}
