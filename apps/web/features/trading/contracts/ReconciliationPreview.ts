import { z } from 'zod';
import { manualSaleReviewSchema } from './ManualSaleReview';
import { cashBaselineReviewSchema } from './CashBaselineReview';

const binding = z.object({
  accountKey: z.string().min(1).max(128),
  rulesHash: z.string().min(1).max(128),
  material: z.string().max(32000),
  previousCashThb: z.string().max(80),
  riskSummary: z.string().max(800),
  originalLot: z
    .object({
      symbol: z.string(),
      quantity: z.string(),
      costThb: z.string(),
      openedAt: z.string(),
    })
    .nullable(),
});

export const reconciliationPreviewSchema = z.discriminatedUnion('kind', [
  z.object({
    kind: z.literal('manual-sale'),
    result: manualSaleReviewSchema,
    binding,
  }),
  z.object({
    kind: z.literal('cash-baseline'),
    result: cashBaselineReviewSchema,
    binding,
  }),
]);

export type ReconciliationPreview = z.infer<typeof reconciliationPreviewSchema>;
