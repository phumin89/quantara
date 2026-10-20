'use client';

import { Component, createRef, type ContextType } from 'react';
import {
  Alert,
  Box,
  Button,
  Drawer,
  IconButton,
  Stack,
  Typography,
} from '@mui/material';
import ArrowBackOutlined from '@mui/icons-material/ArrowBackOutlined';
import CloseOutlined from '@mui/icons-material/CloseOutlined';
import RefreshOutlined from '@mui/icons-material/RefreshOutlined';
import { TeamActivityContext } from '../../context/TeamActivityContext';
import DailyBriefConversation from '../daily-brief-conversation/DailyBriefConversation';
import type { TeamChatWidgetProps } from './TeamChatWidgetProps';

export class TeamChatWidget extends Component<TeamChatWidgetProps> {
  public static override contextType = TeamActivityContext.value;

  declare public context: ContextType<typeof TeamActivityContext.value>;

  private readonly closeButton = createRef<HTMLButtonElement>();

  public override render() {
    const activity = TeamActivityContext.require(this.context);
    const stale =
      activity.fetchedAt === null ||
      activity.error !== null ||
      activity.now - activity.fetchedAt > 30000;
    let status = 'Up to date';
    if (activity.loading) {
      status = 'Checking';
    } else if (stale) {
      status = 'Stale / unavailable';
    }

    return (
      <Drawer
        anchor="right"
        open={this.props.open}
        onClose={this.props.onClose}
        ModalProps={{ keepMounted: true }}
        slotProps={{
          transition: {
            onEntered: () => {
              this.closeButton.current?.focus();
            },
          },
          paper: {
            role: 'dialog',
            'aria-modal': true,
            'aria-labelledby': 'team-chat-title',
            'aria-describedby': 'team-chat-description',
            id: 'team-chat-dialog',
          },
        }}
        sx={{
          '& .MuiDrawer-paper': {
            width: { xs: '100%', md: 560 },
            maxWidth: '100%',
            height: '100dvh',
            borderLeft: { md: '1px solid' },
            borderColor: 'divider',
            borderRadius: 0,
            overflow: 'hidden',
          },
        }}
      >
        <Stack
          direction="row"
          sx={{
            alignItems: 'center',
            gap: 1,
            px: 2,
            py: 1.5,
            borderBottom: '1px solid',
            borderColor: 'divider',
          }}
        >
          <IconButton
            ref={this.closeButton}
            aria-label="Close team chat"
            onClick={this.props.onClose}
            sx={{ width: 44, height: 44 }}
          >
            <ArrowBackOutlined sx={{ display: { xs: 'block', md: 'none' } }} />
            <CloseOutlined sx={{ display: { xs: 'none', md: 'block' } }} />
          </IconButton>
          <Box sx={{ flex: 1, minWidth: 0 }}>
            <Typography
              component="h2"
              id="team-chat-title"
              sx={{ fontSize: 20, fontWeight: 600 }}
            >
              Team chat
            </Typography>
            <Typography
              variant="body2"
              color="text.secondary"
              id="team-chat-description"
            >
              Research reports
            </Typography>
          </Box>
          <IconButton
            aria-label="Refresh team chat"
            onClick={() => {
              void activity.refresh();
            }}
            sx={{ width: 44, height: 44 }}
          >
            <RefreshOutlined />
          </IconButton>
        </Stack>
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ px: 3, py: 1.5 }}
        >
          {status}
          {activity.date ? ` · ${activity.date} Bangkok` : ''}
        </Typography>
        {activity.error && (
          <Alert
            severity="warning"
            sx={{ mx: 2, mb: 2 }}
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
            {activity.error}
          </Alert>
        )}
        <Box sx={{ flex: 1, minHeight: 0 }}>
          <DailyBriefConversation
            key={`${String(activity.scopeRevision)}:${activity.date}`}
            active={this.props.open}
            brief={activity.brief}
            loading={activity.loading}
            error={activity.error}
            date={activity.date}
            embedded
            preservePosition
            idPrefix="team-chat-run-"
          />
        </Box>
      </Drawer>
    );
  }
}
