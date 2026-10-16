import { z } from 'zod';

const adaptiveRiskSchema = z.strictObject({
  profile: z.literal('aggressive'),
  maximumExposureFraction: z.number().gt(0).lte(1),
  maximumConcentrationFraction: z.number().gt(0).lte(1),
  riskToStopFraction: z.number().gt(0).lte(1),
  atrMultiplier: z.number().min(1).max(10),
  maximumSpreadFraction: z.number().min(0).max(0.1),
  estimatedFeeRate: z.number().min(0).max(0.01),
  maximumDataAgeSeconds: z.number().int().min(10).max(300),
  maximumSignalAgeSeconds: z.number().int().min(60).max(900),
});

const adaptivePolicySchema = z.strictObject({
  universeVersion: z.literal('bitkub-thb-spot-v1'),
  includeFutureDeposits: z.literal(true),
  excludedSymbols: z
    .array(z.string().regex(/^[A-Z0-9]{1,30}$/))
    .max(64)
    .refine((symbols) => new Set(symbols).size === symbols.length),
  risk: adaptiveRiskSchema,
  dailyLossFraction: z.number().gt(0).lte(1),
  dailyProfitTargetThb: z.number().positive().max(1_000_000_000),
});

export const tradingRulesSchema = z
  .strictObject({
    version: z.union([
      z.literal('ema20-50-breakout-v1'),
      z.literal('adaptive-intraday-v1'),
    ]),
    symbols: z
      .array(z.string().regex(/^[A-Z0-9]{1,30}$/))
      .min(0)
      .max(16)
      .refine(
        (symbols) => new Set(symbols).size === symbols.length,
        'Symbols must be unique.',
      ),
    allocatedThb: z.number().positive().max(1_000_000_000),
    perOrderThb: z.number().positive(),
    maxPositions: z.number().int().min(1).max(16),
    dailyLossThb: z.number().positive(),
    maxSpreadBps: z.number().min(0).max(1000),
    slippageBps: z.number().min(0).max(1000),
    maxDataAgeSeconds: z.number().int().min(10).max(300),
    takeProfitBps: z.number().min(1).max(10000),
    stopLossBps: z.number().min(1).max(10000),
    trailingStopBps: z.number().min(1).max(10000),
    timeExitMinutes: z.number().int().min(1).max(1440),
    cooldownMinutes: z.number().int().min(0).max(1440),
    orderLifetimeMinutes: z.number().int().min(1).max(1440),
    feeBps: z.number().min(0).max(100),
    volumeRatio: z.number().min(1).max(10),
    adaptive: adaptivePolicySchema.nullable().optional(),
  })
  .refine(
    (rules) =>
      rules.version === 'adaptive-intraday-v1' ||
      rules.adaptive === null ||
      rules.adaptive === undefined,
    'Adaptive policy requires the adaptive trading version.',
  )
  .refine(
    (rules) =>
      rules.version !== 'adaptive-intraday-v1' ||
      (rules.adaptive !== null && rules.adaptive !== undefined),
    'Adaptive trading version requires an adaptive policy.',
  )
  .refine(
    (rules) =>
      rules.version === 'adaptive-intraday-v1' || rules.symbols.length > 0,
    'Legacy trading requires at least one symbol.',
  )
  .refine(
    (rules) =>
      rules.version !== 'adaptive-intraday-v1' || rules.symbols.length === 0,
    'Adaptive trading selects its own eligible markets.',
  )
  .refine(
    (rules) =>
      !rules.adaptive ||
      (rules.adaptive.risk.estimatedFeeRate === rules.feeBps / 10000 &&
        rules.adaptive.risk.maximumSpreadFraction ===
          rules.maxSpreadBps / 10000 &&
        rules.adaptive.risk.maximumDataAgeSeconds === rules.maxDataAgeSeconds),
    'Risk and execution cost, spread and freshness limits must match.',
  )
  .refine(
    (rules) => rules.perOrderThb <= rules.allocatedThb,
    'Per-order amount must not exceed allocated capital.',
  )
  .refine(
    (rules) => rules.dailyLossThb <= rules.allocatedThb,
    'Daily loss cap must not exceed allocated capital.',
  );

export type TradingRules = z.infer<typeof tradingRulesSchema>;
