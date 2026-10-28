import { z } from 'zod';

export const cashBaselineReviewSchema = z.object({
  accountKey: z.string().min(1).max(128),
  cashThb: z
    .string()
    .regex(/^\d+(?:\.\d+)?$/)
    .max(80),
  availableThb: z
    .string()
    .regex(/^\d+(?:\.\d+)?$/)
    .max(80),
  brokerObservedAt: z.iso.datetime({ offset: true }),
  expiresAt: z.iso.datetime({ offset: true }),
  proof: z.string().min(1).max(6000),
  acknowledgement: z.string().min(1).max(400),
  applied: z.boolean(),
  currentDayProfitUnknown: z.boolean(),
});

export type CashBaselineReview = z.infer<typeof cashBaselineReviewSchema>;
