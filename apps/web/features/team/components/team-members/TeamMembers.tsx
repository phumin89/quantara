import { Component, type ReactNode } from 'react';
import {
  Avatar,
  Box,
  Button,
  Chip,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined';
import { TeamPresentation } from '../../presentation/TeamPresentation';
import type { TeamMembersProps } from './TeamMembersProps';

export default class TeamMembers extends Component<TeamMembersProps> {
  public override render(): ReactNode {
    const { latest, membersOpen, onToggle } = this.props;

    return (
      <Paper
        component="aside"
        aria-label="Team members"
        variant="outlined"
        sx={{
          borderRadius: 'var(--radius-panel)',
          order: { xs: 0, lg: 1 },
          overflow: 'hidden',
        }}
      >
        <Button
          onClick={onToggle}
          aria-expanded={membersOpen}
          startIcon={<GroupsOutlinedIcon />}
          sx={{
            display: { xs: 'flex', lg: 'none' },
            width: '100%',
            justifyContent: 'flex-start',
            px: 2,
            minHeight: 44,
            textTransform: 'none',
          }}
        >
          Members · {TeamPresentation.members.length}
        </Button>
        <Box
          sx={{
            p: 'var(--panel-padding)',
            display: { xs: membersOpen ? 'block' : 'none', lg: 'block' },
          }}
        >
          <Typography
            component="h2"
            sx={{ fontSize: 15, fontWeight: 700, mb: 2 }}
          >
            Members{' '}
            <Box
              component="span"
              sx={{ ml: 1, color: 'text.secondary', fontWeight: 400 }}
            >
              {TeamPresentation.members.length}
            </Box>
          </Typography>
          <Stack spacing={2}>
            {TeamPresentation.members.map((member) => {
              const step = latest?.steps.find(
                (item) => item.role === member.role,
              );

              return (
                <Stack
                  key={member.role}
                  direction="row"
                  spacing={1.25}
                  sx={{ alignItems: 'flex-start' }}
                >
                  <Avatar
                    sx={{
                      width: 34,
                      height: 34,
                      fontSize: 14,
                      fontWeight: 700,
                      bgcolor: member.background,
                      color: member.foreground,
                    }}
                  >
                    {member.initials}
                  </Avatar>
                  <Box sx={{ minWidth: 0 }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 700 }}>
                      {member.name}
                    </Typography>
                    <Typography
                      variant="caption"
                      color="text.secondary"
                      sx={{ display: 'block', mb: 0.75 }}
                    >
                      {member.responsibility}
                    </Typography>
                    <Chip
                      size="small"
                      label={step?.status ?? 'Available'}
                      sx={{
                        height: 22,
                        fontSize: 11,
                        textTransform: 'capitalize',
                        ...TeamPresentation.statusStyle(step?.status),
                      }}
                    />
                  </Box>
                </Stack>
              );
            })}
          </Stack>
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{
              display: 'block',
              mt: 2,
              pt: 1.5,
              borderTop: '1px solid',
              borderColor: 'divider',
            }}
          >
            {this.configurationLabel()}
          </Typography>
        </Box>
      </Paper>
    );
  }

  private configurationLabel(): string {
    if (!this.props.overview) {
      return this.props.loading
        ? 'Checking report service…'
        : 'Report service unavailable';
    }

    return this.props.overview.configured
      ? 'Report service configured'
      : 'Report service not configured';
  }
}
