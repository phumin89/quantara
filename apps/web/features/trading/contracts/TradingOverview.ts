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
  paper: bookSchema,
  live: bookSchema,
  reports: z.array(
    z.object({
      date: z.iso.date(),
      mode: z.string().min(1),
      revision: z.number().int().positive(),
      createdAt: timestampSchema,
      text: z.string(),
      status: z.enum(['prepared', 'sent', 'uncertain', 'pending']),
      startingEquityThb: decimalSchema.nullable(),
      endingEquityThb: decimalSchema.nullable(),
      realizedNetThb: decimalSchema.nullable(),
    }),
  ),
});

export type TradingOverview = z.infer<typeof tradingOverviewSchema>;
