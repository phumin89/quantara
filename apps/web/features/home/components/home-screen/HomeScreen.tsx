'use client';

import { Component, type ContextType } from 'react';
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
            mb: 2,
            flexWrap: 'wrap',
          }}
        >
          <Typography
            component="h1"
            variant="h1"
            sx={{ m: 0 }}
          >
            Home
          </Typography>
          <Button
            variant="outlined"
            startIcon={<RefreshOutlined />}
            onClick={() => {
              void activity.refresh();
            }}
          >
            Refresh status
          </Button>
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
            mb: 2,
          }}
        >
          <Chip
            size="small"
            variant="outlined"
            color={fresh ? 'success' : 'default'}
            label={connection}
          />
          <Typography variant="body2">
            {OfficePresentation.company(company)}
          </Typography>
          <Typography
            variant="body2"
            color="text.secondary"
          >
            Checked {OfficePresentation.time(fetchedAt)}
          </Typography>
        </Box>
        <Box
          sx={{
            border: '1px solid',
            borderColor: 'divider',
            borderRadius: '8px',
            overflow: 'hidden',
          }}
        >
          <Box
            component="section"
            aria-labelledby="office-heading"
          >
            <Stack
              direction="row"
              sx={{
                px: 2,
                py: 1,
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 1,
              }}
            >
              <Typography
                component="h2"
                id="office-heading"
                sx={{ fontSize: 18, fontWeight: 600 }}
              >
                Research office
              </Typography>
              <Button
                size="small"
                startIcon={paused ? <PlayArrowOutlined /> : <PauseOutlined />}
                onClick={() => {
                  this.setState({ paused: !paused });
                }}
              >
                {paused ? 'Resume animation' : 'Pause animation'}
              </Button>
            </Stack>
            <OfficeCanvas
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
                xl: 'repeat(4, minmax(0, 1fr))',
              },
              borderTop: '1px solid',
              borderColor: 'divider',
            }}
          >
            {agents.map((agent) => (
              <Button
                key={agent.role}
                aria-pressed={agent.role === selectedRole}
                aria-controls="role-evidence"
                onClick={() => {
                  this.setState({ selectedRole: agent.role });
                }}
                sx={{
                  justifyContent: 'space-between',
                  px: 2,
                  py: 1.5,
                  borderRadius: 0,
                  bgcolor:
                    agent.role === selectedRole
                      ? 'action.selected'
                      : 'transparent',
                  gap: 1,
                  minWidth: 0,
                  flexWrap: 'wrap',
                  alignContent: 'center',
                }}
              >
                <Typography
                  component="span"
                  variant="body2"
                  sx={{ fontWeight: 600 }}
                >
                  {agent.label}
                </Typography>
                <Chip
                  component="span"
                  size="small"
                  label={OfficePresentation.label(agent.status)}
                  color={OfficePresentation.color(agent.status)}
                  variant="outlined"
                />
              </Button>
            ))}
          </Box>
        </Box>
        <Box
          component="section"
          id="role-evidence"
          aria-labelledby="evidence-heading"
          sx={{ mt: 3 }}
        >
          <Stack
            direction="row"
            sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}
          >
            <Typography
              component="h2"
              id="evidence-heading"
              sx={{ fontSize: 22, fontWeight: 600 }}
            >
              {selected?.label ?? 'Role'} evidence
            </Typography>
            {selected && (
              <Chip
                size="small"
                label={OfficePresentation.label(selected.status)}
                color={OfficePresentation.color(selected.status)}
                variant="outlined"
              />
            )}
          </Stack>
          <Typography
            color="text.secondary"
            variant="body2"
            sx={{ mt: 1 }}
          >
            {OfficePresentation.time(selected?.updatedAt)}
          </Typography>
          <Typography
            sx={{
              mt: 1.5,
              maxWidth: '75ch',
              overflowWrap: 'anywhere',
              whiteSpace: 'pre-wrap',
            }}
          >
            {selected?.summary ??
              (loading ? 'Loading…' : 'No recorded work today.')}
          </Typography>
          {company?.error && (
            <Alert
              severity="error"
              sx={{ mt: 2 }}
            >
              {OfficeStateAdapter.clean(company.error)}
            </Alert>
          )}
          {selected?.date && selected.runId && (
            <Link
              component={NextLink}
              href={`/daily-brief?date=${selected.date}&view=team-chat#run-${selected.runId}`}
              sx={{ display: 'inline-block', mt: 1, py: 1.5 }}
            >
              Open recorded report in Daily Brief
            </Link>
          )}
        </Box>
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ mt: 4 }}
        >
          <Link
            href="https://github.com/pixel-agents-hq/pixel-agents"
            target="_blank"
            rel="noopener noreferrer"
          >
            Pixel Agents
          </Link>{' '}
          · <Link href="/vendor/pixel-agents/NOTICE.txt">Credits</Link>
        </Typography>
      </Box>
    );
  }
}
