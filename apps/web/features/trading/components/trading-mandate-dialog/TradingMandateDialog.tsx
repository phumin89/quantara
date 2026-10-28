import { Component, type ReactNode } from 'react';
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Checkbox,
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
        slotProps={{
          paper: {
            sx: {
              borderRadius: 'var(--radius-panel)',
              bgcolor: 'var(--surface-raised)',
              backdropFilter: 'blur(20px)',
              border: '1px solid',
              borderColor: 'var(--border-panel)',
              boxShadow: 'var(--shadow-panel)',
            },
          },
        }}
      >
        <DialogTitle
          id="live-review-title"
          sx={{
            fontFamily: 'var(--font-display)',
            fontSize: 24,
            fontWeight: 850,
            bgcolor: 'var(--color-peach)',
            m: 1.5,
            borderRadius: 'var(--radius-control)',
          }}
        >
          Enable auto trading
        </DialogTitle>
        <DialogContent>{this.renderMandate(overview)}</DialogContent>
        <DialogActions
          sx={{ px: 3, py: 2, borderTop: '1px solid', borderColor: 'divider' }}
        >
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
        {this.props.error && <Alert severity="error">{this.props.error}</Alert>}
        <Typography
          variant="body2"
          sx={{ overflowWrap: 'anywhere' }}
        >
          Account: {overview.accountKey}
        </Typography>
        {TradingRulePresentation.isAdaptive(overview.rules.version) ? (
          <Stack spacing={1}>
            <Typography variant="body2">
              Scope: all eligible Bitkub THB spot markets, excluding{' '}
              {overview.rules.adaptive?.excludedSymbols.join(', ') ||
                'configured exclusions'}
              .
            </Typography>
            <Typography variant="body2">
              Capital: all available THB, including future deposits.
            </Typography>
            <Box
              component="dl"
              sx={{
                m: 0,
                display: 'grid',
                gridTemplateColumns: 'minmax(0, 1fr) auto',
                columnGap: 2,
                rowGap: 0.75,
                fontSize: 14,
                '& dd': {
                  m: 0,
                  textAlign: 'right',
                  fontVariantNumeric: 'tabular-nums',
                },
              }}
            >
              <Box component="dt">Maximum exposure</Box>
              <Box component="dd">
                {TradingFormatting.percent(
                  overview.rules.adaptive?.risk.maximumExposureFraction,
                )}
              </Box>
              <Box component="dt">Maximum concentration</Box>
              <Box component="dd">
                {TradingFormatting.percent(
                  overview.rules.adaptive?.risk.maximumConcentrationFraction,
                )}
              </Box>
              <Box component="dt">Stop-risk scenario</Box>
              <Box component="dd">
                {TradingFormatting.percent(
                  overview.rules.adaptive?.risk.riskToStopFraction,
                )}
              </Box>
              <Box component="dt">Daily loss cap on current equity</Box>
              <Box component="dd">
                {TradingFormatting.percent(
                  overview.rules.adaptive?.dailyLossFraction,
                )}
              </Box>
              <Box component="dt">Maximum positions</Box>
              <Box component="dd">{overview.rules.maxPositions}</Box>
            </Box>
            <Typography variant="body2">
              Strategies: breakout, pullback and range candidates. These are
              limits and scenarios, not a guaranteed maximum loss.
            </Typography>
          </Stack>
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
        {overview.rules.version === 'adaptive-intraday-v2' && (
          <Box
            sx={{
              p: 1.5,
              borderRadius: 'var(--radius-control)',
              border: '1px solid',
              borderColor: 'var(--border-panel)',
              bgcolor: 'var(--surface-soft)',
            }}
          >
            <FormControlLabel
              control={
                <Checkbox
                  checked={this.props.aiManaged}
                  onChange={(event) => {
                    this.props.onAiManagedChange(event.target.checked);
                  }}
                  disabled={this.props.busy}
                />
              }
              label="Let AI manage strategy"
            />
            <Typography
              variant="body2"
              color="text.secondary"
            >
              AI may adjust technical and risk settings within the verified
              account capital ceiling (up to 100%). Spot-only execution checks,
              fees, reconciliation and Stop remain enforced.
            </Typography>
          </Box>
        )}
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
          {this.props.aiManaged
            ? 'Enable AI-managed trading'
            : 'Enable Auto Trading'}
        </Button>
      </Stack>
    );
  }
}
