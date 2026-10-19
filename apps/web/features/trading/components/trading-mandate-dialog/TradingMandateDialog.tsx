import { Component, type ReactNode } from 'react';
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import type { TradingOverview } from '../../contracts/TradingOverview';
import TradingFormatting from '../../presentation/TradingFormatting';
import TradingRulePresentation from '../../presentation/TradingRulePresentation';
import type { TradingMandateDialogProps } from './TradingMandateDialogProps';

export default class TradingMandateDialog extends Component<TradingMandateDialogProps> {
  public override render(): ReactNode {
    const overview = this.props.overview;

    return (
      <Dialog
        open={this.props.open}
        onClose={() => {
          this.props.onClose();
        }}
        maxWidth="sm"
        fullWidth
        aria-labelledby="live-review-title"
      >
        <DialogTitle id="live-review-title">Enable auto trading</DialogTitle>
        <DialogContent>{this.renderMandate(overview)}</DialogContent>
        <DialogActions>
          <Button
            onClick={() => {
              this.props.onClose();
            }}
            sx={{ minHeight: 44 }}
          >
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
    );
  }

  private renderMandate(overview: TradingOverview): ReactNode {
    const expiry = new Date(this.props.expiresAt);
    const validExpiry =
      Number.isFinite(expiry.getTime()) &&
      expiry.getTime() > Date.now() &&
      expiry.getTime() <= Date.now() + 30 * 24 * 60 * 60 * 1000;
    let expiryHelp = 'Choose an expiry to enable auto trading.';
    if (this.props.expiresAt) {
      expiryHelp = 'Expiry must be within the next 30 days.';
      if (
        !Number.isFinite(expiry.getTime()) ||
        expiry.getTime() <= Date.now()
      ) {
        expiryHelp = 'Choose a future date and time.';
      }

      if (validExpiry) {
        expiryHelp = `Expires ${TradingFormatting.time(expiry.toISOString())}`;
      }
    }

    return (
      <Stack
        spacing={1.5}
        sx={{ pt: 1 }}
      >
        <Alert severity="warning">
          Real money. The engine will buy and sell within these limits until
          expiry, without asking for each trade. AI reports do not authorize
          orders.
        </Alert>
        <Typography
          variant="body2"
          sx={{ overflowWrap: 'anywhere' }}
        >
          Account: {overview.accountKey}
        </Typography>
        {TradingRulePresentation.isAdaptive(overview.rules.version) ? (
          <Alert severity="info">
            Scope: all eligible Bitkub THB spot markets, excluding{' '}
            {overview.rules.adaptive?.excludedSymbols.join(', ') ||
              'configured exclusions'}
            . Capital: all available THB, including future deposits. Risk: up to{' '}
            {TradingFormatting.percent(
              overview.rules.adaptive?.risk.maximumExposureFraction,
            )}{' '}
            exposure and{' '}
            {TradingFormatting.percent(
              overview.rules.adaptive?.risk.maximumConcentrationFraction,
            )}{' '}
            concentration, with a{' '}
            {TradingFormatting.percent(
              overview.rules.adaptive?.risk.riskToStopFraction,
            )}{' '}
            stop-risk scenario and{' '}
            {TradingFormatting.percent(
              overview.rules.adaptive?.dailyLossFraction,
            )}{' '}
            daily loss cap on current equity. These are limits and scenarios,
            not a guaranteed maximum loss. Strategies: breakout, pullback and
            range candidates. Maximum positions: {overview.rules.maxPositions}.
          </Alert>
        ) : (
          <Box
            component="dl"
            sx={{
              m: 0,
              display: 'grid',
              gridTemplateColumns: 'minmax(0, 1fr) auto',
              columnGap: 2,
              rowGap: 0.5,
              fontSize: 14,
            }}
          >
            {TradingRulePresentation.fields.slice(0, 4).map((field) => (
              <Box
                key={field.key}
                sx={{ display: 'contents' }}
              >
                <Box component="dt">{field.label}</Box>
                <Box
                  component="dd"
                  sx={{
                    m: 0,
                    textAlign: 'right',
                    fontVariantNumeric: 'tabular-nums',
                  }}
                >
                  {overview.rules[field.key]}
                </Box>
              </Box>
            ))}
          </Box>
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
            {TradingRulePresentation.isAdaptive(overview.rules.version)
              ? 'Strategy & account policy'
              : 'Strategy & all limits'}
          </Box>
          <Typography
            variant="body2"
            sx={{ overflowWrap: 'anywhere', mb: 1 }}
          >
            Strategy: {overview.rules.version}
            <br />
            Rules hash: {overview.rulesHash}
          </Typography>
          <Box
            component="dl"
            sx={{
              m: 0,
              fontSize: 14,
              display: 'grid',
              gridTemplateColumns: 'minmax(0, 1fr) auto',
              gap: 1,
            }}
          >
            {!TradingRulePresentation.isAdaptive(overview.rules.version) &&
              TradingRulePresentation.fields.slice(4).map((field) => (
                <Box
                  key={field.key}
                  sx={{ display: 'contents' }}
                >
                  <Box component="dt">{field.label.replace(' (bps)', '')}</Box>
                  <Box
                    component="dd"
                    sx={{
                      m: 0,
                      textAlign: 'right',
                      fontVariantNumeric: 'tabular-nums',
                    }}
                  >
                    {field.key.endsWith('Bps')
                      ? `${String(overview.rules[field.key] / 100)}%`
                      : overview.rules[field.key]}
                  </Box>
                </Box>
              ))}
          </Box>
        </Box>
        <Typography
          component="h3"
          sx={{ fontSize: 14, fontWeight: 600 }}
        >
          When should permission expire?
        </Typography>
        <Stack
          direction="row"
          spacing={1}
        >
          {[1, 7, 30].map((days) => (
            <Button
              key={days}
              variant="outlined"
              disabled={this.props.busy}
              onClick={() => {
                this.props.onExpiresAtChange(
                  TradingFormatting.localDateTime(
                    Date.now() + days * 24 * 60 * 60 * 1000,
                  ),
                );
              }}
              sx={{ minHeight: 44 }}
            >
              {days} {days === 1 ? 'day' : 'days'}
            </Button>
          ))}
        </Stack>
        <TextField
          type="datetime-local"
          label="Mandate expiry (your local time)"
          required
          error={!!this.props.expiresAt && !validExpiry}
          value={this.props.expiresAt}
          onChange={(event) => {
            this.props.onExpiresAtChange(event.target.value);
          }}
          slotProps={{
            inputLabel: { shrink: true },
            htmlInput: {
              min: TradingFormatting.localDateTime(Date.now()),
              max: TradingFormatting.localDateTime(
                Date.now() + 30 * 24 * 60 * 60 * 1000,
              ),
            },
          }}
          helperText={expiryHelp}
          disabled={this.props.busy}
        />
        <Button
          variant="contained"
          disableElevation
          disabled={
            this.props.busy ||
            this.props.dirty ||
            !!this.props.error ||
            !validExpiry ||
            !overview.liveActivationAvailable ||
            overview.emergencyStopped ||
            overview.mode === 'autoLive'
          }
          onClick={() => {
            this.props.onActivate();
          }}
          sx={{ alignSelf: 'flex-start', minHeight: 44 }}
        >
          Enable Auto Trading
        </Button>
      </Stack>
    );
  }
}
