'use client';

import { Component, createRef, type ContextType } from 'react';
import NextLink from 'next/link';
import {
  Alert,
  Box,
  Button,
  Chip,
  Link,
  Stack,
  Typography,
} from '@mui/material';
import RefreshOutlined from '@mui/icons-material/RefreshOutlined';
import PauseOutlined from '@mui/icons-material/PauseOutlined';
import PlayArrowOutlined from '@mui/icons-material/PlayArrowOutlined';
import { TeamActivityContext } from '../../../team/context/TeamActivityContext';
import { TeamRole } from '../../../team/contracts/TeamRole';
import { OfficeStateAdapter } from '../../presentation/OfficeStateAdapter';
import { OfficePresentation } from '../../presentation/OfficePresentation';
import { OfficeCanvas } from '../office-canvas/OfficeCanvas';
import type { HomeScreenState } from './HomeScreenState';

export default class HomeScreen extends Component<
  Record<string, never>,
  HomeScreenState
> {
  public static override contextType = TeamActivityContext.value;

  declare public context: ContextType<typeof TeamActivityContext.value>;

  public override state: HomeScreenState = {
    selectedRole: TeamRole.Reviewer,
    paused: false,
  };

  private initialRoleSelected = false;

  private readonly office = createRef<OfficeCanvas>();

  public override componentDidMount(): void {
    this.selectInitialRole();
  }

  public override componentDidUpdate(): void {
    this.selectInitialRole();
  }

  private selectInitialRole(): void {
    const activity = TeamActivityContext.require(this.context);
    if (!this.initialRoleSelected && activity.fetchedAt !== null) {
      this.initialRoleSelected = true;
      this.setState({
        selectedRole:
          activity.overview?.company?.activeRole ??
          activity.overview?.latestRun?.steps[0]?.role ??
          TeamRole.Reviewer,
      });
    }
  }

  public override render() {
    const activity = TeamActivityContext.require(this.context);
    const { overview, loading, error, fetchedAt, now } = activity;
    const { selectedRole, paused } = this.state;
    const fresh =
      fetchedAt !== null && error === null && now - fetchedAt <= 30000;
    const agents = OfficeStateAdapter.project(
      overview,
      activity.brief?.runs ?? [],
      fresh,
    );
    const selected = agents.find((agent) => agent.role === selectedRole);
    const company = overview?.company;
    let connection = fresh ? 'Connected' : 'Stale / unavailable';
    if (loading) {
      connection = 'Checking';
    }

    return (
      <Box sx={{ width: '100%', maxWidth: 1440 }}>
        <Stack
          direction="row"
          sx={{
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 2,
            mb: 'var(--section-gap)',
            flexWrap: 'wrap',
          }}
        >
          <Box sx={{ minWidth: 0, flex: { xs: '1 1 100%', sm: '1 1 auto' } }}>
            <Typography
              component="h1"
              variant="h1"
              sx={{
                m: 0,
                fontFamily: 'var(--font-display)',
                fontSize: 32,
                fontWeight: 900,
              }}
            >
              Home
            </Typography>
            <Typography
              variant="body1"
              color="text.secondary"
            >
              Your research team at work
            </Typography>
          </Box>
          <Stack
            direction="row"
            sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}
          >
            <Link
              component={NextLink}
              href="/daily-brief"
              underline="none"
              sx={{
                display: 'inline-flex',
                alignItems: 'center',
                minHeight: 44,
                px: 2,
                color: 'text.primary',
                bgcolor: 'var(--color-peach)',
                border: '1px solid',
                borderColor: 'var(--border-panel)',
                borderRadius: 'var(--radius-control)',
                fontSize: 14,
                fontWeight: 800,
                '&:hover': { bgcolor: 'var(--color-mint)' },
              }}
            >
              Read research reports
            </Link>
            <Button
              variant="outlined"
              startIcon={<RefreshOutlined />}
              onClick={() => {
                void activity.refresh();
              }}
              sx={{ bgcolor: 'var(--surface-raised)' }}
            >
              Refresh status
            </Button>
          </Stack>
        </Stack>
        {error && (
          <Alert
            severity="warning"
            sx={{ mb: 2 }}
          >
            {error}
          </Alert>
        )}
        <Box
          component="section"
          aria-label="Research operation"
          sx={{
            display: 'flex',
            gap: 1.5,
            alignItems: 'center',
            flexWrap: 'wrap',
            bgcolor: 'var(--surface-panel)',
            border: '1px solid',
            borderColor: 'var(--border-panel)',
            borderRadius: 'var(--radius-panel)',
            p: 'var(--panel-padding)',
            mb: 'var(--section-gap)',
          }}
        >
          <Chip
            size="small"
            variant="outlined"
            color={fresh ? 'success' : 'default'}
            label={connection}
            sx={{ borderRadius: 'var(--radius-tag)', fontWeight: 800 }}
          />
          <Typography variant="body2">
            {OfficePresentation.company(company)}
          </Typography>
          <Typography
            variant="body2"
            color="text.secondary"
            sx={{ ml: { md: 'auto' }, fontVariantNumeric: 'tabular-nums' }}
          >
            Checked {OfficePresentation.time(fetchedAt)}
          </Typography>
        </Box>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: {
              xs: 'minmax(0, 1fr)',
              lg: 'minmax(0, 1fr) 280px',
            },
            gap: 'var(--section-gap)',
            alignItems: 'start',
          }}
        >
          <Box
            component="section"
            aria-labelledby="office-heading"
            sx={{
              minWidth: 0,
              border: '1px solid',
              borderColor: 'var(--border-panel)',
              borderRadius: 'var(--radius-panel)',
              bgcolor: 'var(--surface-panel)',
              boxShadow: 'var(--shadow-panel)',
              p: 'var(--panel-padding)',
            }}
          >
            <Stack
              direction="row"
              sx={{
                mb: 2,
                minHeight: 44,
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 1,
                flexWrap: 'wrap',
              }}
            >
              <Typography
                component="h2"
                id="office-heading"
                sx={{ fontSize: 20, fontWeight: 900 }}
              >
                Research office
              </Typography>
              <Button
                size="small"
                startIcon={paused ? <PlayArrowOutlined /> : <PauseOutlined />}
                onClick={() => {
                  this.setState({ paused: !paused });
                }}
                sx={{
                  minHeight: 44,
                  px: 1.5,
                  bgcolor: 'var(--surface-soft)',
                  borderRadius: 'var(--radius-control)',
                  '&:hover': { bgcolor: 'var(--color-mint)' },
                }}
              >
                {paused ? 'Resume animation' : 'Pause animation'}
              </Button>
            </Stack>
            <OfficeCanvas
              ref={this.office}
              agents={agents}
              selectedId={selected?.id ?? null}
              paused={paused}
              onSelect={(id) => {
                const agent = agents.find((item) => item.id === id);
                if (agent) {
                  this.setState({ selectedRole: agent.role });
                }
              }}
            />
          </Box>
          <Box
            component="section"
            aria-label="Team"
            sx={{
              display: 'grid',
              gridTemplateColumns: {
                xs: 'repeat(2, minmax(0, 1fr))',
                sm: 'repeat(auto-fit, minmax(140px, 1fr))',
                lg: 'minmax(0, 1fr)',
              },
              gap: 0.75,
              border: '1px solid',
              borderColor: 'var(--border-panel)',
              borderRadius: 'var(--radius-panel)',
              bgcolor: 'var(--surface-panel)',
              boxShadow: 'var(--shadow-panel)',
              p: 'var(--panel-padding)',
            }}
          >
            <Typography
              component="h2"
              sx={{
                gridColumn: '1 / -1',
                m: 0,
                minHeight: 44,
                alignContent: 'center',
                fontSize: 20,
                fontWeight: 900,
              }}
            >
              Team
            </Typography>
            {agents.map((agent) => (
              <Button
                key={agent.role}
                aria-pressed={agent.role === selectedRole}
                onClick={() => {
                  this.setState({ selectedRole: agent.role });
                  this.office.current?.focusAgent(agent.id);
                }}
                sx={{
                  justifyContent: 'center',
                  flexDirection: 'column',
                  alignItems: 'flex-start',
                  px: 2,
                  py: 1.5,
                  minHeight: 88,
                  border: '1px solid',
                  borderColor:
                    agent.role === selectedRole
                      ? 'text.primary'
                      : 'var(--border-panel)',
                  borderRadius: 'var(--radius-control)',
                  bgcolor:
                    agent.role === selectedRole
                      ? 'var(--color-lilac)'
                      : 'var(--surface-raised)',
                  color: 'text.primary',
                  gap: 0.5,
                  minWidth: 0,
                  textAlign: 'left',
                  '&:hover': { bgcolor: 'var(--color-mint)' },
                  '&.Mui-focusVisible': {
                    outline: '2px solid',
                    outlineColor: 'info.main',
                    outlineOffset: 2,
                  },
                }}
              >
                <Typography
                  component="span"
                  variant="body2"
                  sx={{ fontWeight: 800 }}
                >
                  {agent.label}
                </Typography>
                <Typography
                  component="span"
                  variant="caption"
                  color={
                    agent.role === selectedRole
                      ? 'text.primary'
                      : 'text.secondary'
                  }
                  sx={{ fontWeight: agent.role === selectedRole ? 800 : 600 }}
                >
                  {OfficePresentation.label(agent.status)}
                </Typography>
              </Button>
            ))}
          </Box>
        </Box>
        <Typography
          component="div"
          variant="body2"
          color="text.secondary"
          sx={{
            display: 'flex',
            flexWrap: 'wrap',
            gap: 2,
            mt: 3,
            '& a': {
              display: 'inline-flex',
              alignItems: 'center',
              minHeight: 44,
            },
          }}
        >
          <Link
            href="https://github.com/pixel-agents-hq/pixel-agents"
            target="_blank"
            rel="noopener noreferrer"
          >
            Pixel Agents
          </Link>
          <Link href="/vendor/pixel-agents/NOTICE.txt">Credits</Link>
        </Typography>
      </Box>
    );
  }
}
