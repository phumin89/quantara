import { z } from 'zod';
import { tradingRulesSchema } from './TradingRules';

const decimalSchema = z
  .string()
  .regex(/^-?\d+(?:\.\d+)?$/)
  .refine((value) => Number.isFinite(Number(value)));
const timestampSchema = z.iso.datetime({ offset: true });
const exitPlanSchema = z.strictObject({
  version: z.literal('adaptive-exit-v1'),
  regime: z.enum(['trend', 'range', 'unknown']),
  timeframeSeconds: z.union([z.literal(300), z.literal(900), z.literal(3600)]),
  nextReviewAt: timestampSchema,
  hardExitAt: timestampSchema,
  effectiveStop: decimalSchema,
  trailingActive: z.boolean(),
  reviewCount: z.number().int().nonnegative(),
  revision: z.number().int().positive(),
  lastReason: z.string().max(500),
});
const notificationsSchema = z.strictObject({
  pending: z.number().int().nonnegative(),
  sending: z.number().int().nonnegative(),
  sent: z.number().int().nonnegative(),
  unknown: z.number().int().nonnegative(),
  blocked: z.number().int().nonnegative().optional(),
  oldestPendingAt: timestampSchema.nullable(),
  state: z.enum(['unconfigured', 'ready', 'pending', 'delayed', 'needsReview']),
  dashboardAvailable: z.boolean().optional(),
});
const exitStudySchema = z.strictObject({
  protocol: z.literal('paired-exit-v1'),
  status: z.enum(['collecting', 'readyForReview', 'insufficientEvidence']),
  verifiedProfit: z.literal(false),
  startedAt: timestampSchema.nullable(),
  observedAt: timestampSchema,
  liveSamples: z.number().int().nonnegative(),
  paperSamples: z.number().int().nonnegative(),
  pairedClosed: z.number().int().nonnegative(),
  unknown: z.number().int().nonnegative(),
  minimumDays: z.literal(14),
  minimumClosed: z.literal(30),
  hypotheticalNetThb: decimalSchema.nullable(),
  baselineNetThb: decimalSchema.nullable(),
});
const readinessSchema = z.strictObject({
  readyMarkets: z.number().int().nonnegative().max(10000),
  unavailableMarkets: z.number().int().nonnegative().max(10000),
  frames: z
    .array(
      z.strictObject({
        symbol: z.string().min(1).max(30),
        timeframeSeconds: z.union([
          z.literal(300),
          z.literal(900),
          z.literal(3600),
        ]),
        checkedAt: timestampSchema,
        source: z.literal('bitkub-tradingview'),
        status: z.enum(['ready', 'unavailable']),
        gapCause: z.enum(['none', 'unknown', 'source-unavailable']),
        verifiedZeroVolumeBars: z.number().int().nonnegative().max(120),
        completedBars: z.number().int().nonnegative().max(100000),
        contiguousBars: z.number().int().nonnegative().max(100000),
        missingIntervals: z.number().int().nonnegative().max(100000),
        reason: z.string().max(500).nullable(),
      }),
    )
    .max(1000),
});
const funnelSchema = z.strictObject({
  date: z.iso.date(),
  coverage: z.enum(['complete', 'retained']),
  signals: z.number().int().nonnegative().max(1000000),
  submittedOrders: z.number().int().nonnegative().max(1000000),
  filledOrders: z.number().int().nonnegative().max(1000000),
  closedTrades: z.number().int().nonnegative().max(1000000),
  netThb: decimalSchema.nullable(),
  blockedReasons: z
    .array(
      z.strictObject({
        reason: z.string().min(1).max(500),
        count: z.number().int().nonnegative().max(1000000),
      }),
    )
    .max(100),
});
const bookSchema = z.object({
  cashThb: decimalSchema,
  settledCashThb: decimalSchema.nullable().optional(),
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
      exitPlan: exitPlanSchema.nullable().optional(),
      exitPolicy: z.unknown().optional(),
      stopPrice: decimalSchema.optional(),
      targetPrice: decimalSchema.optional(),
      trailingDistance: decimalSchema.optional(),
      trailingActive: z.boolean().optional(),
      timeExitAt: timestampSchema.nullable().optional(),
    }),
  ),
  orders: z.array(
    z.object({
      id: z.string().min(1),
      symbol: z.string().min(1),
      side: z.enum(['buy', 'sell']),
      amount: decimalSchema,
      limitPrice: decimalSchema,
      status: z.enum([
        'pending',
        'submitting',
        'submitted',
        'partial',
        'unknown',
        'filled',
        'cancelled',
        'rejected',
        'expired',
      ]),
      createdAt: timestampSchema,
      brokerOrderId: z.string().nullable(),
      reason: z.string(),
      originalAmount: decimalSchema.nullable().optional(),
      lastExecutionMessage: z.string().max(500).nullable().optional(),
      lastExecutionAt: timestampSchema.nullable().optional(),
      cancellationState: z
        .enum(['claimed', 'unknown', 'acknowledged', 'confirmed'])
        .nullable()
        .optional(),
      lastInspectedAt: timestampSchema.nullable().optional(),
      lastInspectionError: z.string().max(500).nullable().optional(),
      remaining: decimalSchema,
    }),
  ),
});
export const tradingOverviewSchema = z.object({
  accountKey: z.string().min(1),
  objective: z
    .strictObject({
      id: z.string().min(1),
      targetThb: z.number(),
      deadlineExclusive: z.iso.datetime({ offset: true }),
      includesDeposits: z.literal(true),
      timeZone: z.literal('Asia/Bangkok'),
      observedAt: timestampSchema.nullable(),
      portfolioValueThb: decimalSchema.nullable(),
      remainingThb: decimalSchema.nullable(),
      depositsThb: decimalSchema.nullable(),
      tradingPnlThb: decimalSchema.nullable(),
      valuationStatus: z.enum(['verified', 'unknown']),
      reason: z.string().nullable(),
      accountingNote: z.string().max(500).nullable().optional(),
      measurement: z.string().max(120).nullable().optional(),
      valuationProtocol: z.string().max(120).nullable().optional(),
      maximumAgeSeconds: z.number().int().nonnegative().nullable().optional(),
    })
    .optional(),
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
  aiPolicy: z
    .strictObject({
      enabled: z.boolean(),
      capitalCeilingFraction: z.literal(1),
      expiresAt: timestampSchema.nullable(),
      originalRulesHash: z.string().min(1),
      effectiveRulesHash: z.string().min(1),
      status: z.string().min(1),
      reason: z.string().min(1),
      lastChangedAt: timestampSchema.nullable(),
    })
    .nullable()
    .optional(),
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
                open: z.number().int().nonnegative().optional(),
                expired: z.number().int().nonnegative().optional(),
                closedUnknown: z.number().int().nonnegative().optional(),
                meanNetReturnFraction: z.number().nullable(),
              }),
            )
            .max(7),
        })
        .optional(),
      realizedNetTodayThb: decimalSchema.nullable(),
      readiness: readinessSchema.nullable().optional(),
      funnel: funnelSchema.nullable().optional(),
      evaluation: z
        .strictObject({
          protocol: z.enum([
            'ohlc-scenario-v1',
            'ohlc-scenario-v2',
            'ohlc-scenario-v3',
          ]),
          verifiedProfit: z.literal(false),
          observedAt: timestampSchema,
          rulesHash: z.string().regex(/^[a-f0-9]{64}$/),
          warnings: z.array(z.string().max(1200)).max(30),
          metrics: z
            .array(
              z.strictObject({
                symbol: z.string().regex(/^[A-Z0-9]{1,30}$/),
                timeframeSeconds: z.union([
                  z.literal(300),
                  z.literal(900),
                  z.literal(3600),
                ]),
                segment: z.enum(['training', 'validation']),
                strategy: z.enum(['breakout', 'pullback', 'range']),
                samples: z.number().int().nonnegative(),
                trades: z.number().int().nonnegative(),
                unknownFills: z.number().int().nonnegative(),
                netThb: z.number().nullable(),
                feesThb: z.number().nonnegative(),
                maxDrawdownThb: z.number().nonnegative().nullable(),
                cashBenchmarkThb: z.literal(0),
                insufficientSamples: z.boolean(),
              }),
            )
            .max(72),
        })
        .nullable()
        .optional(),
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
  notifications: notificationsSchema.optional(),
  exitStudy: exitStudySchema.optional(),
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
