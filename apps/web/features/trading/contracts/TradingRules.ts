import { z } from 'zod';

export const tradingRulesSchema = z
  .strictObject({
    version: z.literal('ema20-50-breakout-v1'),
    symbols: z
      .array(z.string().regex(/^[A-Z0-9]{1,30}$/))
      .min(1)
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
  })
  .refine(
    (rules) => rules.perOrderThb <= rules.allocatedThb,
    'Per-order amount must not exceed allocated capital.',
  )
  .refine(
    (rules) => rules.dailyLossThb <= rules.allocatedThb,
    'Daily loss cap must not exceed allocated capital.',
  );

export type TradingRules = z.infer<typeof tradingRulesSchema>;
