import { Component, type ReactNode } from 'react';
import { Box, Button, IconButton, Tooltip } from '@mui/material';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import TradingRulePresentation from '../../presentation/TradingRulePresentation';
import type { TradingControlsProps } from './TradingControlsProps';

export default class TradingControls extends Component<TradingControlsProps> {
  public override render(): ReactNode {
    const overview = this.props.overview;

    return (
      <Box
        sx={{
          display: 'flex',
          gap: 1,
          flexWrap: 'wrap',
          '& .MuiButton-root': { minHeight: 44 },
        }}
      >
        {!this.props.liveOn && overview.liveActivationAvailable && (
          <Button
            variant="contained"
            disableElevation
            disabled={
              this.props.busy ||
              this.props.dirty ||
              !!this.props.error ||
              overview.emergencyStopped ||
              overview.mode === 'autoLive'
            }
            onClick={() => {
              this.props.onReview();
            }}
          >
            {TradingRulePresentation.isAdaptive(overview.rules.version)
              ? 'Review live mandate'
              : 'Set up and review'}
          </Button>
        )}
        <Tooltip title="Trading settings">
          <IconButton
            aria-label="Trading settings"
            onClick={() => {
              this.props.onSettings();
            }}
            sx={{
              width: 44,
              height: 44,
              border: '1px solid #e0e0e0',
              borderRadius: '6px',
            }}
          >
            <SettingsOutlinedIcon fontSize="small" />
          </IconButton>
        </Tooltip>
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
          color="error"
          variant="outlined"
          disabled={this.props.busy}
          onClick={() => {
            this.props.onEmergencyStop();
          }}
        >
          Emergency stop
        </Button>
      </Box>
    );
  }
}
