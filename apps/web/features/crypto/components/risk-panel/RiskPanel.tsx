'use client';

import { Component, type ContextType, type ReactNode } from 'react';
import {
  Alert,
  Button,
  FormControl,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import RefreshOutlinedIcon from '@mui/icons-material/RefreshOutlined';
import SaveOutlinedIcon from '@mui/icons-material/SaveOutlined';
import { RiskServiceContext } from '../../context/RiskServiceContext';
import type { RiskPolicy } from '../../contracts/RiskPolicy';
import type { RiskPanelState } from './RiskPanelState';

export default class RiskPanel extends Component<object, RiskPanelState> {
  public static override readonly contextType = RiskServiceContext.value;

  public override context: ContextType<typeof RiskServiceContext.value> = null;

  public override state: RiskPanelState = {
    overview: null,
    policy: null,
    loading: true,
    busy: false,
    error: null,
    basePolicyHash: null,
    dirty: false,
  };

  private mounted: boolean = false;

  private request: AbortController | null = null;

  private action: AbortController | null = null;

  private generation: number = 0;

  public override componentDidMount(): void {
    this.mounted = true;
    void this.load();
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.generation += 1;
    this.request?.abort();
    this.action?.abort();
  }

  public override render(): ReactNode {
    const { overview, policy, loading, busy, error } = this.state;

    return (
      <Paper
        variant="outlined"
        sx={{ p: { xs: 2, sm: 2.5 }, borderRadius: '8px' }}
      >
        <Stack spacing={1.5}>
          <Stack
            direction="row"
            spacing={1}
            sx={{ alignItems: 'center', justifyContent: 'space-between' }}
          >
            <Typography
              component="h2"
              sx={{ fontSize: 16, fontWeight: 700 }}
            >
              Risk sizing
            </Typography>
            <Button
              size="small"
              variant="outlined"
              startIcon={<RefreshOutlinedIcon />}
              disabled={busy || loading}
              onClick={() => void this.refresh()}
              sx={{ textTransform: 'none', minHeight: 44 }}
            >
              Refresh capital
            </Button>
          </Stack>
          <Typography
            variant="caption"
            color="text.secondary"
          >
            Manual risk sizing for future proposals. Current Auto Trading is
            unchanged.
          </Typography>
          {error && <Alert severity="error">{error}</Alert>}
          {this.renderContent(overview, policy, loading, busy)}
        </Stack>
      </Paper>
    );
  }

  private renderContent(
    overview: RiskPanelState['overview'],
    policy: RiskPolicy | null,
    loading: boolean,
    busy: boolean,
  ): ReactNode {
    if (loading && !overview) {
      return (
        <Typography color="text.secondary">
          Loading saved risk overview…
        </Typography>
      );
    }

    if (overview && policy) {
      return this.renderOverview(overview, policy, busy);
    }

    return (
      <Button
        onClick={() => {
          void this.load();
        }}
        sx={{ alignSelf: 'flex-start', textTransform: 'none' }}
      >
        Retry risk overview
      </Button>
    );
  }

  private renderOverview(
    overview: NonNullable<RiskPanelState['overview']>,
    policy: RiskPolicy,
    busy: boolean,
  ): ReactNode {
    return (
      <Stack spacing={1.5}>
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          spacing={2}
        >
          <FormControl
            size="small"
            sx={{ minWidth: 180 }}
          >
            <InputLabel id="risk-profile-label">Risk profile</InputLabel>
            <Select
              labelId="risk-profile-label"
              label="Risk profile"
              value={policy.profile}
              disabled={busy}
              onChange={(event) => {
                this.setPolicy({
                  profile: event.target.value,
                });
              }}
            >
              <MenuItem value="disabled">Disabled</MenuItem>
              <MenuItem value="aggressive">Aggressive</MenuItem>
            </Select>
          </FormControl>
          <Button
            variant="contained"
            startIcon={<SaveOutlinedIcon />}
            disabled={busy || !this.state.dirty}
            onClick={() => void this.save()}
            sx={{ textTransform: 'none', minHeight: 44 }}
          >
            Save policy
          </Button>
        </Stack>
        <Stack
          direction="row"
          sx={{ flexWrap: 'wrap', gap: 2 }}
        >
          {this.metric('Equity', overview.capital?.equityThb ?? null)}
          {this.metric('Available', overview.capital?.spendableThb ?? null)}
          {this.metric('Exposure', overview.capital?.exposureThb ?? null)}
          {this.metric('Reserved', overview.capital?.reservedThb ?? null)}
        </Stack>
        {overview.capital?.observedAt && (
          <Typography
            variant="caption"
            color="text.secondary"
          >
            Evidence observed{' '}
            {new Date(overview.capital.observedAt).toLocaleString('en-GB', {
              timeZone: 'Asia/Bangkok',
            })}{' '}
            Bangkok
          </Typography>
        )}
        {(overview.capital?.blocker ||
          overview.capital?.hasUncertainOrders) && (
          <Alert severity="warning">
            {overview.capital.blocker ??
              'Uncertain orders require reconciliation before sizing.'}
          </Alert>
        )}
        <details>
          <Typography
            component="summary"
            variant="body2"
            sx={{
              cursor: 'pointer',
              fontWeight: 600,
              minHeight: 44,
              alignContent: 'center',
            }}
          >
            Advanced policy
          </Typography>
          <Stack
            spacing={1.25}
            sx={{ pt: 1.5 }}
          >
            <Stack
              direction={{ xs: 'column', sm: 'row' }}
              spacing={1.5}
            >
              <TextField
                size="small"
                label="Exposure %"
                type="number"
                disabled={busy}
                value={this.percent(policy.maximumExposureFraction)}
                onChange={(event) => {
                  this.setPolicy({
                    maximumExposureFraction: this.fraction(event.target.value),
                  });
                }}
              />
              <TextField
                size="small"
                label="Concentration %"
                type="number"
                disabled={busy}
                value={this.percent(policy.maximumConcentrationFraction)}
                onChange={(event) => {
                  this.setPolicy({
                    maximumConcentrationFraction: this.fraction(
                      event.target.value,
                    ),
                  });
                }}
              />
              <TextField
                size="small"
                label="Risk to stop %"
                type="number"
                disabled={busy}
                value={this.percent(policy.riskToStopFraction)}
                onChange={(event) => {
                  this.setPolicy({
                    riskToStopFraction: this.fraction(event.target.value),
                  });
                }}
              />
              <TextField
                size="small"
                label="ATR multiplier"
                type="number"
                disabled={busy}
                value={policy.atrMultiplier}
                onChange={(event) => {
                  this.setPolicy({ atrMultiplier: Number(event.target.value) });
                }}
              />
            </Stack>
            <Typography
              variant="caption"
              color="text.secondary"
            >
              Defaults: 100% exposure · 100% concentration · 5% risk to stop · 1
              ATR · 0.5% spread · 0.25% fee · 90s data · 600s signal.
            </Typography>
          </Stack>
        </details>
        {overview.performance && (
          <Typography
            variant="caption"
            color="text.secondary"
          >
            Net equity change (excluding transfers):{' '}
            {overview.performance.netEquityChangeThb ?? 'Unknown'} THB ·
            Flow-free return:{' '}
            {overview.performance.timeWeightedReturn === null
              ? 'Unknown'
              : `${(Number(overview.performance.timeWeightedReturn) * 100).toFixed(2)}%`}{' '}
            · {overview.performance.explanation}
          </Typography>
        )}
      </Stack>
    );
  }

  private metric(label: string, value: string | null): ReactNode {
    return (
      <Typography variant="body2">
        <strong>{label}</strong> {value === null ? 'Unknown' : `${value} THB`}
      </Typography>
    );
  }

  private setPolicy(changes: Partial<RiskPolicy>): void {
    this.setState((current) =>
      current.policy
        ? { policy: { ...current.policy, ...changes }, dirty: true }
        : null,
    );
  }

  private percent(value: number): string {
    return String(value * 100);
  }

  private fraction(value: string): number {
    return Number(value) / 100;
  }

  private async load(): Promise<void> {
    if (this.action) {
      return;
    }

    const generation = ++this.generation;
    this.request?.abort();
    const request = new AbortController();
    this.request = request;
    this.setState({ loading: true, error: null });
    try {
      const overview = await RiskServiceContext.require(this.context).get(
        request.signal,
      );
      if (
        this.mounted &&
        !request.signal.aborted &&
        generation === this.generation
      ) {
        const preserveDraft = this.state.dirty && this.state.policy;
        this.setState({
          overview,
          policy: preserveDraft ? this.state.policy : overview.policy,
          basePolicyHash: preserveDraft
            ? this.state.basePolicyHash
            : overview.policyHash,
          dirty: Boolean(preserveDraft),
        });
      }
    } catch (cause) {
      if (
        this.mounted &&
        !request.signal.aborted &&
        generation === this.generation
      ) {
        this.setState({
          error:
            cause instanceof Error
              ? cause.message
              : 'Unable to load risk overview.',
        });
      }
    } finally {
      if (
        this.mounted &&
        !request.signal.aborted &&
        generation === this.generation
      ) {
        this.setState({ loading: false });
      }
    }
  }

  private async refresh(): Promise<void> {
    if (this.state.busy || this.action) {
      return;
    }

    const action = new AbortController();
    this.action = action;
    this.setState({ busy: true, error: null });
    try {
      const overview = await RiskServiceContext.require(this.context).refresh(
        action.signal,
      );
      if (this.mounted && !action.signal.aborted) {
        this.setState((current) => ({
          overview,
          policy: current.dirty ? current.policy : overview.policy,
          basePolicyHash: current.dirty
            ? current.basePolicyHash
            : overview.policyHash,
        }));
      }
    } catch (cause) {
      if (this.mounted && !action.signal.aborted) {
        this.setState({
          error:
            cause instanceof Error
              ? cause.message
              : 'Unable to refresh risk evidence.',
        });
      }
    } finally {
      if (this.mounted && !action.signal.aborted) {
        this.setState({ busy: false });
      }

      this.action = null;
    }
  }

  private async save(): Promise<void> {
    const { overview, policy } = this.state;
    if (this.state.busy || this.action || !overview || !policy) {
      return;
    }

    const action = new AbortController();
    this.action = action;
    this.setState({ busy: true, error: null });
    try {
      const next = await RiskServiceContext.require(this.context).save(
        policy,
        this.state.basePolicyHash ?? overview.policyHash,
        action.signal,
      );
      if (this.mounted && !action.signal.aborted) {
        this.setState({
          overview: next,
          policy: next.policy,
          basePolicyHash: next.policyHash,
          dirty: false,
        });
      }
    } catch (cause) {
      if (this.mounted && !action.signal.aborted) {
        this.setState({
          error:
            cause instanceof Error
              ? cause.message
              : 'Unable to save risk policy.',
        });
      }
    } finally {
      if (this.mounted && !action.signal.aborted) {
        this.setState({ busy: false });
      }

      this.action = null;
    }
  }
}
