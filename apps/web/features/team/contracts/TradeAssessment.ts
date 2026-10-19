import { z } from 'zod';

const timestampSchema = z.iso.datetime({ offset: true });

export const tradeAssessmentSchema = z.strictObject({
  schemaVersion: z.literal('research-assessment-v1'),
  accountKey: z.string().min(1),
  runId: z.uuid(),
  policyHash: z.string().nullable(),
  asOf: timestampSchema,
  expiresAt: timestampSchema,
  evidenceIds: z.array(z.string().min(1).max(128)).max(32),
  positions: z
    .array(
      z.strictObject({
        symbol: z.string().min(1).max(30),
        action: z.enum(['Keep', 'Reduce', 'Exit', 'Review']),
        thesis: z.string().min(1).max(1200),
        reason: z.string().min(1).max(1200),
        reviewAfter: timestampSchema,
        expiresAt: timestampSchema,
        missingFields: z.array(z.string().max(120)).max(32),
      }),
    )
    .max(64),
  opportunities: z
    .array(
      z.strictObject({
        symbol: z.string().min(1).max(30),
        reason: z.string().min(1).max(1200),
        reconsiderWhen: z.string().min(1).max(1200),
        reviewAfter: timestampSchema,
      }),
    )
    .max(64),
});

export type TradeAssessment = z.infer<typeof tradeAssessmentSchema>;
