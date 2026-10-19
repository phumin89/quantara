import type { TradingOverview } from '../contracts/TradingOverview';

export default class TradingRulePresentation {
  public static isAdaptive(
    version: TradingOverview['rules']['version'],
  ): boolean {
    return (
      version === 'adaptive-intraday-v1' ||
      version === 'adaptive-intraday-v2' ||
      version === 'adaptive-intraday-v3'
    );
  }

  public static readonly fields = [
    {
      key: 'allocatedThb',
      label: 'Allocated capital (THB)',
      min: 0.01,
      max: undefined,
      step: 'any',
    },
    {
      key: 'perOrderThb',
      label: 'Per-order amount (THB)',
      min: 0.01,
      max: undefined,
      step: 'any',
    },
    {
      key: 'maxPositions',
      label: 'Maximum positions',
      min: 1,
      max: undefined,
      step: '1',
    },
    {
      key: 'dailyLossThb',
      label: 'Daily loss cap (THB)',
      min: 0.01,
      max: undefined,
      step: 'any',
    },
    {
      key: 'maxSpreadBps',
      label: 'Maximum spread (bps)',
      min: 0,
      max: 1000,
      step: 'any',
    },
    {
      key: 'slippageBps',
      label: 'Slippage cap (bps)',
      min: 0,
      max: 1000,
      step: 'any',
    },
    {
      key: 'maxDataAgeSeconds',
      label: 'Maximum data age (seconds)',
      min: 10,
      max: 300,
      step: '1',
    },
    {
      key: 'takeProfitBps',
      label: 'Take profit (bps)',
      min: 1,
      max: 10000,
      step: 'any',
    },
    {
      key: 'stopLossBps',
      label: 'Stop loss (bps)',
      min: 1,
      max: 10000,
      step: 'any',
    },
    {
      key: 'trailingStopBps',
      label: 'Trailing stop (bps)',
      min: 1,
      max: 10000,
      step: 'any',
    },
    {
      key: 'timeExitMinutes',
      label: 'Time exit (minutes)',
      min: 1,
      max: 1440,
      step: '1',
    },
    {
      key: 'cooldownMinutes',
      label: 'Cooldown (minutes)',
      min: 0,
      max: undefined,
      step: '1',
    },
    {
      key: 'orderLifetimeMinutes',
      label: 'Order lifetime (minutes)',
      min: 1,
      max: undefined,
      step: '1',
    },
    { key: 'feeBps', label: 'Fee model (bps)', min: 0, max: 100, step: 'any' },
    {
      key: 'volumeRatio',
      label: 'Entry volume ratio',
      min: 1,
      max: 10,
      step: 'any',
    },
  ] as const;
}
