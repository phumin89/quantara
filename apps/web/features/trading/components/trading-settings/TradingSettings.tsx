import { Component, createRef, type ReactNode } from 'react';
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Divider,
  Drawer,
  IconButton,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import CloseOutlinedIcon from '@mui/icons-material/CloseOutlined';
import type { TradingOverview } from '../../contracts/TradingOverview';
import TradingRulePresentation from '../../presentation/TradingRulePresentation';
import TradingPolicySummary from '../trading-policy-summary/TradingPolicySummary';
import type { TradingSettingsProps } from './TradingSettingsProps';

export default class TradingSettings extends Component<TradingSettingsProps> {
  private readonly closeButton = createRef<HTMLButtonElement>();

  public override componentDidUpdate(previous: TradingSettingsProps): void {
    const contextChanged =
      previous.overview.accountKey !== this.props.overview.accountKey ||
      previous.overview.rulesHash !== this.props.overview.rulesHash;
    if (
      ((previous.error && !this.props.error) || contextChanged) &&
      this.props.open &&
      document.activeElement === document.body
    ) {
      this.closeButton.current?.focus();
    }
  }

  public override render(): ReactNode {
    const overview = this.props.overview;

    return (
      <Drawer
        anchor="right"
        open={this.props.open}
        onClose={() => {
          this.props.onClose();
        }}
        ModalProps={{ keepMounted: true }}
        slotProps={{
          paper: {
            role: 'dialog',
            'aria-modal': true,
            'aria-labelledby': 'trading-settings-title',
            sx: {
              width: { xs: '100%', sm: 600 },
              maxWidth: '100%',
              p: 'var(--panel-padding)',
              borderRadius: 'var(--radius-panel) 0 0 var(--radius-panel)',
              bgcolor: 'var(--surface-raised)',
              backdropFilter: 'blur(20px)',
              borderLeft: '1px solid',
              borderColor: 'var(--border-panel)',
              boxShadow: 'var(--shadow-panel)',
              display: 'flex',
              flexDirection: 'column',
              overflow: 'hidden',
            },
          },
        }}
      >
        <Stack
          direction="row"
          sx={{
            alignItems: 'center',
            justifyContent: 'space-between',
            mb: 1,
            p: 1.5,
            bgcolor: 'var(--color-lilac)',
            borderRadius: 'var(--radius-control)',
            flexShrink: 0,
          }}
        >
          <Typography
            id="trading-settings-title"
            component="h2"
            sx={{
              fontFamily: 'var(--font-display)',
              fontSize: 24,
              fontWeight: 850,
            }}
          >
            Trading settings
          </Typography>
          <IconButton
            ref={this.closeButton}
            aria-label="Close trading settings"
            onClick={() => {
              this.props.onClose();
            }}
            sx={{ width: 44, height: 44 }}
          >
            <CloseOutlinedIcon />
          </IconButton>
        </Stack>
        {this.props.error && (
          <Alert
            severity="error"
            sx={{ mb: 2, flexShrink: 0 }}
          >
            <AlertTitle>Trading status unavailable</AlertTitle>
            {this.props.error}
            <Typography
              variant="body2"
              sx={{ mt: 1 }}
            >
              Refresh status before saving or changing operating mode.
            </Typography>
            <Button
              color="inherit"
              variant="outlined"
              disabled={this.props.busy || this.props.refreshing}
              loading={this.props.refreshing}
              onClick={() => {
                this.props.onRetry();
              }}
              sx={{ mt: 1.5, minHeight: 44 }}
            >
              Refresh status
            </Button>
          </Alert>
        )}
        <Box
          role="region"
          aria-labelledby="trading-settings-title"
          sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}
        >
          {this.renderConfiguration(overview)}
          <Divider sx={{ my: 3 }} />
          {TradingRulePresentation.isAdaptive(overview.rules.version) ? (
            <Box
              component="details"
              sx={{
                '& > summary': {
                  cursor: 'pointer',
                  minHeight: 44,
                  alignContent: 'center',
                  fontSize: 14,
                  fontWeight: 600,
                },
              }}
            >
              <Box component="summary">Operating mode</Box>
              {this.renderOperatingMode(overview)}
            </Box>
          ) : (
            this.renderOperatingMode(overview)
          )}
        </Box>
      </Drawer>
    );
  }

  private renderOperatingMode(overview: TradingOverview): ReactNode {
    return (
      <Stack spacing={1}>
        {!TradingRulePresentation.isAdaptive(overview.rules.version) && (
          <Typography
            component="h3"
            sx={{ fontSize: 15, fontWeight: 700, mb: 1 }}
          >
            Operating mode
          </Typography>
        )}
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ mb: 1 }}
        >
          Switching mode disarms live trading. Paper uses simulated funds;
          manual review requires approval for each trade.
        </Typography>
        <Stack
          direction="row"
          spacing={1}
        >
          <Button
            variant="outlined"
            disabled={
              this.props.busy || !!this.props.error || overview.mode === 'paper'
            }
            onClick={() => {
              this.props.onSetMode('paper');
            }}
          >
            Paper
          </Button>
          <Button
            variant="outlined"
            disabled={
              this.props.busy ||
              !!this.props.error ||
              overview.mode === 'manualReview'
            }
            onClick={() => {
              this.props.onSetMode('manualReview');
            }}
          >
            Manual review
          </Button>
        </Stack>
      </Stack>
    );
  }

  private renderConfiguration(overview: TradingOverview): ReactNode {
    const adaptive = TradingRulePresentation.isAdaptive(overview.rules.version);

    return (
      <Box
        key={`${overview.accountKey}:${overview.rulesHash}`}
        component="form"
        onChange={() => {
          this.props.onDirty();
        }}
        onInvalidCapture={(event) => {
          if (event.target instanceof HTMLElement) {
            const disclosure = event.target.closest('details');
            if (disclosure) {
              disclosure.open = true;
            }
          }
        }}
        onSubmit={(event) => {
          event.preventDefault();
          this.props.onSave(new FormData(event.currentTarget));
        }}
      >
        <Stack
          spacing={2}
          sx={{ pt: 1 }}
        >
          {this.props.validationError && (
            <Alert severity="error">{this.props.validationError}</Alert>
          )}
          {adaptive ? (
            <>
              <TradingPolicySummary
                overview={overview}
                showRiskLimits={false}
              />
              <Typography
                component="h3"
                sx={{ fontSize: 15, fontWeight: 700 }}
              >
                Your risk limits
              </Typography>
              <Typography
                variant="body2"
                color="text.secondary"
              >
                The saved strategy automatically selects eligible markets,
                timeframe and size within these limits. Saved technical defaults
                are ready to use; AI research does not change limits.
              </Typography>
              {this.renderAdaptiveRiskFields(overview)}
              {this.renderSaveWarning()}
              {this.renderSaveButton('Save risk limits and disarm live')}
              {overview.rules.version === 'adaptive-intraday-v1' && (
                <Button
                  variant="outlined"
                  disabled={
                    this.props.busy || this.props.dirty || !!this.props.error
                  }
                  onClick={() => {
                    this.props.onPrepareAdaptiveSetup();
                  }}
                  sx={{ alignSelf: 'flex-start', minHeight: 44 }}
                >
                  Prepare v2 upgrade and review
                </Button>
              )}
            </>
          ) : (
            <>
              <Typography
                variant="body2"
                color="text.secondary"
              >
                The engine follows these saved rules. The AI team researches
                separately.
              </Typography>
              <Button
                variant="outlined"
                disabled={
                  this.props.busy || this.props.dirty || !!this.props.error
                }
                onClick={() => {
                  this.props.onPrepareAdaptiveSetup();
                }}
                sx={{ alignSelf: 'flex-start', minHeight: 44 }}
              >
                Set up and review autonomous trading
              </Button>
            </>
          )}
          {!adaptive && (
            <>
              {this.renderSaveWarning()}
              <Typography
                component="h3"
                sx={{ fontSize: 15, fontWeight: 700 }}
              >
                Budget &amp; markets
              </Typography>
              <input
                type="hidden"
                name="version"
                value={overview.rules.version}
              />
              <TextField
                label="Allowed symbols"
                name="symbols"
                defaultValue={overview.rules.symbols.join(', ')}
                required
                helperText="For example BTC, ETH. Up to 16 symbols; THB spot only."
                disabled={this.props.busy}
              />
              {this.renderRuleFields(overview, false)}
            </>
          )}
          <Box
            component="details"
            sx={{
              '& > summary': {
                cursor: 'pointer',
                minHeight: 44,
                alignContent: 'center',
                fontSize: 14,
                fontWeight: 600,
              },
            }}
          >
            <Box component="summary">
              {adaptive
                ? 'Advanced strategy settings'
                : 'Advanced settings · strategy & execution'}
            </Box>
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ my: 2 }}
            >
              {adaptive
                ? 'Strategy parameters remain fixed in the saved rules. 100 basis points (bps) = 1%.'
                : 'EMA 20/50 breakout v1 · 5-minute candles. 100 basis points (bps) = 1%.'}
            </Typography>
            {adaptive && (
              <Stack
                spacing={2}
                sx={{ mb: 2 }}
              >
                {this.renderRuleFields(overview, false)}
              </Stack>
            )}
            {this.renderRuleFields(overview, true)}
            {overview.rules.version === 'adaptive-intraday-v3' && (
              <>
                {(
                  [
                    'takeProfitBps',
                    'stopLossBps',
                    'trailingStopBps',
                    'timeExitMinutes',
                  ] as const
                ).map((key) => (
                  <input
                    key={key}
                    type="hidden"
                    name={key}
                    value={overview.rules[key]}
                  />
                ))}
                <Typography
                  variant="body2"
                  color="text.secondary"
                >
                  Experimental v3 exits follow the saved trend and range review
                  policy. Compatibility take profit, stop loss, trailing stop
                  and time exit values remain unchanged by this form.
                </Typography>
              </>
            )}
          </Box>
          {!adaptive && this.renderSaveButton('Save rules and disarm live')}
        </Stack>
      </Box>
    );
  }

  private renderSaveWarning(): ReactNode {
    return (
      <Alert severity="warning">
        Saving overrides turns auto trading off. Review and enable again to use
        the new rules with real money.
      </Alert>
    );
  }

  private renderAdaptiveRiskFields(overview: TradingOverview): ReactNode {
    if (!overview.rules.adaptive) {
      return null;
    }

    return (
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' },
          gap: 2,
        }}
      >
        {(
          [
            [
              'maximumExposureFraction',
              'Maximum exposure (%)',
              overview.rules.adaptive.risk.maximumExposureFraction,
            ],
            [
              'maximumConcentrationFraction',
              'Maximum concentration (%)',
              overview.rules.adaptive.risk.maximumConcentrationFraction,
            ],
            [
              'riskToStopFraction',
              'Risk to stop scenario (%)',
              overview.rules.adaptive.risk.riskToStopFraction,
            ],
            [
              'dailyLossFraction',
              'Daily loss cap (% of current equity)',
              overview.rules.adaptive.dailyLossFraction,
            ],
          ] as const
        ).map(([name, label, value]) => (
          <TextField
            key={name}
            name={name}
            label={label}
            type="number"
            defaultValue={value * 100}
            required
            disabled={this.props.busy}
            slotProps={{ htmlInput: { min: 0.01, max: 100, step: 'any' } }}
          />
        ))}
      </Box>
    );
  }

  private renderSaveButton(label: string): ReactNode {
    return (
      <Button
        variant="contained"
        disableElevation
        type="submit"
        disabled={this.props.busy || !this.props.dirty || !!this.props.error}
        sx={{ alignSelf: 'flex-start', minHeight: 44 }}
      >
        {label}
      </Button>
    );
  }

  private renderRuleFields(
    overview: TradingOverview,
    advanced: boolean,
  ): ReactNode {
    return (
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: {
            xs: 'minmax(0, 1fr)',
            sm: 'repeat(2, minmax(0, 1fr))',
          },
          gap: 2,
        }}
      >
        {(advanced
          ? TradingRulePresentation.fields.slice(4)
          : TradingRulePresentation.fields.slice(0, 4)
        )
          .filter(
            (field) =>
              advanced ||
              !TradingRulePresentation.isAdaptive(overview.rules.version) ||
              field.key === 'maxPositions',
          )
          .filter(
            (field) =>
              !(
                advanced &&
                overview.rules.version === 'adaptive-intraday-v3' &&
                [
                  'takeProfitBps',
                  'stopLossBps',
                  'trailingStopBps',
                  'timeExitMinutes',
                ].includes(field.key)
              ),
          )
          .map((field) => (
            <TextField
              key={field.key}
              label={field.label}
              name={field.key}
              type="number"
              defaultValue={overview.rules[field.key]}
              required
              disabled={this.props.busy}
              slotProps={{
                htmlInput: {
                  min: field.min,
                  max: field.max,
                  step: field.step,
                },
              }}
            />
          ))}
      </Box>
    );
  }
}
