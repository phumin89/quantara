import { z } from 'zod';
import { positionSchema } from './Position';

export const portfolioSchema = z.object({
  positions: z.array(positionSchema),
  summary: z.object({
    totalAssets: z.number().nullable(),
    availableThb: z.number().nullable(),
    holdingCount: z.number().nullable(),
  }),
  updatedAt: z.string().nullable(),
  priceUpdatedAt: z.string().nullable(),
  historyUpdatedAt: z.string().nullable(),
  historyCoverage: z.string(),
  syncing: z.boolean(),
  completedSymbols: z.number(),
  totalSymbols: z.number(),
  error: z.string().nullable(),
});

export type PortfolioResponse = z.infer<typeof portfolioSchema>;
