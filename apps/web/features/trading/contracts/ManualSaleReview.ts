import { z } from 'zod';

const amount = z
  .string()
  .regex(/^\d+(?:\.\d+)?$/)
  .max(80);

export const manualSaleReviewSchema = z.object({
  symbol: z.string().regex(/^[A-Z0-9]{1,30}$/),
  allocatedSoldQuantity: amount,
  unallocatedHistoricalQuantity: amount,
  allocatedCostThb: amount.nullable(),
  realizedNetProfitThb: z
    .string()
    .regex(/^-?\d+(?:\.\d+)?$/)
    .nullable(),
  brokerBalance: z.object({
    symbol: z.string(),
    quantity: amount,
    available: amount.nullable(),
  }),
  brokerObservedAt: z.iso.datetime({ offset: true }),
  expiresAt: z.iso.datetime({ offset: true }),
  proof: z.string().min(1).max(6000),
  acknowledgement: z.string().min(1).max(400),
  applied: z.boolean(),
  cashSettlementPending: z.boolean(),
});

export type ManualSaleReview = z.infer<typeof manualSaleReviewSchema>;
