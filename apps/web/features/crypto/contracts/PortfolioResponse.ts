import { z } from 'zod';
import { positionSchema } from './Position';
import { portfolioPerformanceSchema } from './PortfolioPerformance';

export const portfolioSchema = z
  .object({
    positions: z.array(positionSchema),
    summary: z.object({
      totalAssets: z.number().nullable(),
      availableThb: z.number().nullable(),
      holdingCount: z.number().int().nonnegative().nullable(),
    }),
    performance: portfolioPerformanceSchema.nullable().optional(),
    updatedAt: z.iso.datetime({ offset: true }).nullable(),
    priceUpdatedAt: z.iso.datetime({ offset: true }).nullable(),
    historyUpdatedAt: z.iso.datetime({ offset: true }).nullable(),
    historyCoverage: z.string(),
    syncing: z.boolean(),
    completedSymbols: z.number().int().nonnegative(),
    totalSymbols: z.number().int().nonnegative(),
    error: z.string().nullable(),
  })
  .refine(
    (portfolio) =>
      new Set(
        portfolio.positions.map((position) => position.symbol.toUpperCase()),
      ).size === portfolio.positions.length,
    { message: 'Position symbols must be unique regardless of case.' },
  );

export type PortfolioResponse = z.infer<typeof portfolioSchema>;
