import { Component, type ReactNode } from 'react';
import { Box, Button } from '@mui/material';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import TradingRulePresentation from '../../presentation/TradingRulePresentation';
import type { TradingControlsProps } from './TradingControlsProps';

export default class TradingControls extends Component<TradingControlsProps> {
  public override render(): ReactNode {
    const overview = this.props.overview;
    let reviewLabel = TradingRulePresentation.isAdaptive(overview.rules.version)
      ? 'Review & enable'
      : 'Set up and review';
    if (this.props.balanceReviewNeeded) {
      reviewLabel = 'Review balances';
    }

    if (this.props.orderReviewNeeded) {
      reviewLabel = 'Review orders';
    }

    return (
      <Box
        sx={{
          display: 'flex',
          gap: 1.25,
          flexWrap: 'wrap',
          alignItems: 'center',
          '& .MuiButton-root': {
            minHeight: 44,
            textTransform: 'none',
            borderRadius: 'var(--radius-control)',
            fontWeight: 800,
          },
        }}
      >
        {(!this.props.liveOn ||
          this.props.orderReviewNeeded ||
          this.props.balanceReviewNeeded) &&
          (overview.liveActivationAvailable ||
            this.props.balanceReviewNeeded ||
            this.props.orderReviewNeeded) && (
            <Button
              variant="contained"
              disableElevation
              disabled={
                this.props.busy ||
                (!this.props.orderReviewNeeded &&
                  !this.props.balanceReviewNeeded &&
                  (this.props.dirty ||
                    !!this.props.error ||
                    (overview.emergencyStopped &&
                      !this.props.balanceReviewNeeded) ||
                    overview.mode === 'autoLive'))
              }
              onClick={() => {
                this.props.onReview();
              }}
            >
              {reviewLabel}
            </Button>
          )}
        <Button
          variant="outlined"
          aria-label="Trading settings"
          onClick={() => {
            this.props.onSettings();
          }}
          startIcon={<SettingsOutlinedIcon fontSize="small" />}
        >
          Settings
        </Button>
        {(overview.mode === 'autoLive' || overview.mode === 'manualReview') && (
          <Button
            variant="outlined"
            disabled={this.props.busy}
            onClick={() => {
              this.props.onDisable();
            }}
          >
            Disable
          </Button>
        )}
        <Button
          color={overview.emergencyStopped ? 'primary' : 'error'}
          variant="outlined"
          disabled={
            this.props.busy || (overview.emergencyStopped && !!this.props.error)
          }
          sx={{ ml: { sm: 'auto' } }}
          onClick={() => {
            if (overview.emergencyStopped) {
              this.props.onDisable();
            } else {
              this.props.onEmergencyStop();
            }
          }}
        >
          {overview.emergencyStopped
            ? 'Clear emergency stop'
            : 'Emergency stop'}
        </Button>
      </Box>
    );
  }
}
