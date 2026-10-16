import { z } from 'zod';
import { riskPolicySchema } from './RiskPolicy';

const money = z.string().regex(/^\d+(?:\.\d+)?$/);

const capitalSchema = z
  .object({
    accountKey: z.string().min(1),
    observedAt: z.iso.datetime({ offset: true }),
    spendableThb: money.nullable(),
    equityThb: money.nullable(),
    exposureThb: money.nullable(),
    reservedThb: money.nullable(),
    hasUncertainOrders: z.boolean(),
    blocker: z.string().nullable(),
  })
  .strict();

const performanceSchema = z
  .object({
    from: z.iso.datetime({ offset: true }),
    to: z.iso.datetime({ offset: true }),
    netEquityChangeThb: z
      .string()
      .regex(/^-?\d+(?:\.\d+)?$/)
      .nullable(),
    timeWeightedReturn: z
      .string()
      .regex(/^-?\d+(?:\.\d+)?$/)
      .nullable(),
    explanation: z.string(),
  })
  .strict();

export const riskOverviewSchema = z
  .object({
    accountKey: z.string().min(1),
    policyHash: z.string().regex(/^[a-f0-9]{64}$/),
    policy: riskPolicySchema,
    capital: capitalSchema.nullable(),
    performance: performanceSchema.nullable(),
    refreshError: z.string().nullable(),
    manualOnly: z.literal(true),
  })
  .strict();

export type RiskOverview = z.infer<typeof riskOverviewSchema>;
