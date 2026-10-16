import { z } from 'zod';

const fraction = z.number().gt(0).lte(1);

export const riskPolicySchema = z
  .object({
    profile: z.enum(['disabled', 'aggressive']),
    maximumExposureFraction: fraction,
    maximumConcentrationFraction: fraction,
    riskToStopFraction: fraction,
    atrMultiplier: z.number().min(1).max(10),
    maximumSpreadFraction: z.number().min(0).max(0.1),
    estimatedFeeRate: z.number().min(0).max(0.01),
    maximumDataAgeSeconds: z.number().int().min(10).max(300),
    maximumSignalAgeSeconds: z.number().int().min(60).max(900),
  })
  .strict();

export type RiskPolicy = z.infer<typeof riskPolicySchema>;
