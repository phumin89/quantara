import { Component, type ReactNode } from 'react';
import {
  Alert,
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
import type { TradingSettingsProps } from './TradingSettingsProps';

export default class TradingSettings extends Component<TradingSettingsProps> {
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
              p: { xs: 2, sm: 3 },
              borderRadius: 0,
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
            position: 'sticky',
            top: 0,
            bgcolor: 'background.paper',
            zIndex: 1,
          }}
        >
          <Typography
            id="trading-settings-title"
            component="h2"
            sx={{ fontSize: 20, fontWeight: 700 }}
          >
            Trading settings
          </Typography>
          <IconButton
            aria-label="Close trading settings"
            onClick={() => {
              this.props.onClose();
            }}
            sx={{ width: 44, height: 44 }}
          >
            <CloseOutlinedIcon />
          </IconButton>
        </Stack>
        <Box
          role="region"
          aria-labelledby="trading-settings-title"
        >
          {this.renderConfiguration(overview)}
        </Box>
        <Divider sx={{ my: 3 }} />
        <Typography
          component="h3"
          sx={{ fontSize: 15, fontWeight: 700, mb: 1 }}
        >
          Operating mode
        </Typography>
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
      </Drawer>
    );
  }

  private renderConfiguration(overview: TradingOverview): ReactNode {
    return (
      <Box
        key={overview.rulesHash}
        component="form"
        onChange={() => {
          this.props.onDirty();
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
          <Typography
            variant="body2"
            color="text.secondary"
          >
            Set your budget and limits here. The engine follows the saved rules;
            the AI team researches separately.
          </Typography>
          {TradingRulePresentation.isAdaptive(overview.rules.version) ? (
            <Alert severity="info">
              Adaptive mode scans eligible THB spot markets and sizes orders
              from verified account capital. Fixed symbols and per-order amounts
              are not used.
              {overview.rules.version === 'adaptive-intraday-v1' && (
                <Button
                  size="small"
                  onClick={() => {
                    this.props.onStageAdaptiveUpgrade();
                  }}
                  sx={{ ml: 1, minHeight: 44 }}
                >
                  Upgrade strategy
                </Button>
              )}
            </Alert>
          ) : (
            <Button
              variant="outlined"
              disabled={this.props.busy || !!this.props.error}
              onClick={() => {
                this.props.onPrepareAdaptiveSetup();
              }}
              sx={{ alignSelf: 'flex-start', minHeight: 44 }}
            >
              Set up and review autonomous trading
            </Button>
          )}
          <Alert severity="warning">
            Saving changes turns auto trading off. Review and enable again to
            use the new rules with real money.
          </Alert>
          {!TradingRulePresentation.isAdaptive(overview.rules.version) && (
            <>
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
              Advanced settings · strategy &amp; execution
            </Box>
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ my: 2 }}
            >
              {TradingRulePresentation.isAdaptive(overview.rules.version)
                ? 'Adaptive execution settings are derived from the verified account and market policy.'
                : 'EMA 20/50 breakout v1 · 5-minute candles. 100 basis points (bps) = 1%.'}
            </Typography>
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
                  Adaptive exits: trend and range reviews use the installed
                  adaptive exit policy. Derived take profit, stop loss, trailing
                  stop and time exit values are retained for compatibility.
                </Typography>
              </>
            )}
            {overview.rules.adaptive && (
              <Box
                sx={{
                  display: 'grid',
                  gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' },
                  gap: 2,
                  mt: 2,
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
                    slotProps={{
                      htmlInput: { min: 0.01, max: 100, step: 'any' },
                    }}
                  />
                ))}
              </Box>
            )}
          </Box>
          <Button
            variant="contained"
            disableElevation
            type="submit"
            disabled={
              this.props.busy || !this.props.dirty || !!this.props.error
            }
            sx={{ alignSelf: 'flex-start', minHeight: 44 }}
          >
            Save rules and disarm live
          </Button>
        </Stack>
      </Box>
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
