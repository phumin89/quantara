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
          borderRadius: '8px',
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
          Members · 3
        </Button>
        <Box
          sx={{
            p: 2.5,
            display: { xs: membersOpen ? 'block' : 'none', lg: 'block' },
          }}
        >
          <Typography
            component="h2"
            sx={{ fontSize: 15, fontWeight: 700, mb: 3 }}
          >
            Members{' '}
            <Box
              component="span"
              sx={{ ml: 1, color: 'text.secondary', fontWeight: 400 }}
            >
              3
            </Box>
          </Typography>
          <Stack spacing={3}>
            {TeamPresentation.members.map((member) => {
              const step = latest?.steps.find(
                (item) => item.role === member.role,
              );

              return (
                <Stack
                  key={member.role}
                  direction="row"
                  spacing={1.5}
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
                      label={step?.status ?? 'No task'}
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
              mt: 3,
              pt: 2,
              borderTop: '1px solid #e0e0e0',
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
