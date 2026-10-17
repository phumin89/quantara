import { z } from 'zod';
import { tradingRulesSchema } from './TradingRules';

const decimalSchema = z
  .string()
  .regex(/^-?\d+(?:\.\d+)?$/)
  .refine((value) => Number.isFinite(Number(value)));
const timestampSchema = z.iso.datetime({ offset: true });
const bookSchema = z.object({
  cashThb: decimalSchema,
  equityThb: decimalSchema.nullable(),
  realizedGrossThb: decimalSchema.nullable(),
  realizedNetThb: decimalSchema.nullable(),
  feesThb: decimalSchema,
  unrealizedThb: decimalSchema.nullable(),
  positions: z.array(
    z.object({
      symbol: z.string().min(1),
      quantity: decimalSchema,
      costThb: decimalSchema,
      openedAt: timestampSchema,
      peakBid: decimalSchema,
    }),
  ),
  orders: z.array(
    z.object({
      id: z.string().min(1),
      symbol: z.string().min(1),
      side: z.enum(['buy', 'sell']),
      amount: decimalSchema,
      limitPrice: decimalSchema,
      status: z.string().min(1),
      createdAt: timestampSchema,
      brokerOrderId: z.string().nullable(),
      reason: z.string(),
      remaining: decimalSchema,
    }),
  ),
});
export const tradingOverviewSchema = z.object({
  accountKey: z.string().min(1),
  mode: z.enum(['stopped', 'paper', 'manualReview', 'autoLive']),
  rules: tradingRulesSchema,
  rulesHash: z.string().min(1),
  mandate: z
    .object({
      id: z.string().min(1),
      accountKey: z.string().min(1),
      rulesHash: z.string().min(1),
      createdAt: timestampSchema,
      expiresAt: timestampSchema,
      revokedAt: timestampSchema.nullable(),
    })
    .nullable(),
  emergencyStopped: z.boolean(),
  heartbeatAt: timestampSchema.nullable(),
  fault: z.string().nullable(),
  liveActivationAvailable: z.boolean(),
  autonomy: z
    .strictObject({
      observedAt: timestampSchema,
      universeAt: timestampSchema.nullable(),
      totalMarkets: z.number().int().nonnegative(),
      eligibleMarkets: z.number().int().nonnegative(),
      checkedMarkets: z.number().int().nonnegative(),
      activity: z.string().min(1),
      nextCheckAt: timestampSchema.nullable(),
      capital: z
        .strictObject({
          observedAt: timestampSchema,
          availableThb: decimalSchema.nullable(),
          equityThb: decimalSchema.nullable(),
          exposureThb: decimalSchema.nullable(),
          reservedThb: decimalSchema.nullable(),
          blocker: z.string().nullable(),
        })
        .nullable(),
      candidates: z.array(
        z.strictObject({
          symbol: z.string().min(1),
          at: timestampSchema,
          reason: z.string().min(1),
          strategyVersion: z.string().nullable(),
          score: decimalSchema.nullable().optional(),
        }),
      ),
      dailyProfitTargetThb: decimalSchema,
      opportunityReview: z
        .strictObject({
          protocol: z.literal('observed-book-v1'),
          sampleLimit: z.number().int().min(1).max(8),
          assumptions: z.string().min(1).max(1000),
          days: z
            .array(
              z.strictObject({
                date: z.iso.date(),
                captured: z.number().int().nonnegative(),
                closed: z.number().int().nonnegative(),
                waiting: z.number().int().nonnegative(),
                unknown: z.number().int().nonnegative(),
                meanNetReturnFraction: z.number().nullable(),
              }),
            )
            .max(7),
        })
        .optional(),
      realizedNetTodayThb: decimalSchema.nullable(),
    })
    .nullable()
    .optional(),
  realtime: z
    .object({
      connected: z.boolean(),
      lastEventAt: timestampSchema.nullable(),
      watchedSymbols: z.array(z.string().regex(/^[A-Z0-9]{1,30}$/)).max(64),
      dirtySymbols: z.array(z.string().regex(/^[A-Z0-9]{1,30}$/)).max(64),
      coalesced: z.number().int().nonnegative(),
      dropped: z.number().int().nonnegative(),
      decisionP50Ms: z.number().nonnegative().nullable(),
      decisionP95Ms: z.number().nonnegative().nullable(),
      lastExitEvaluationAt: timestampSchema.nullable(),
      lastReconciliationAt: timestampSchema.nullable(),
      lastSubmitLatencyMs: z.number().nonnegative().nullable(),
    })
    .nullable()
    .optional(),
  paper: bookSchema,
  live: bookSchema,
  reports: z.array(
    z.object({
      date: z.iso.date(),
      mode: z.string().min(1),
      revision: z.number().int().positive(),
      createdAt: timestampSchema,
      text: z.string(),
      status: z.enum(['prepared', 'sent', 'uncertain', 'pending', 'local']),
      startingEquityThb: decimalSchema.nullable(),
      endingEquityThb: decimalSchema.nullable(),
      realizedNetThb: decimalSchema.nullable(),
    }),
  ),
});

export type TradingOverview = z.infer<typeof tradingOverviewSchema>;
