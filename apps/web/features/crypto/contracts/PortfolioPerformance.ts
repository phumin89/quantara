import { z } from 'zod';

export const portfolioPerformanceSchema = z.object({
  scope: z.literal('confirmedEngineFills'),
  realizedPnl: z.number().nullable(),
  unrealizedPnl: z.number().nullable(),
  totalPnl: z.number().nullable(),
  fees: z.number().nonnegative().nullable(),
  asOf: z.iso.datetime({ offset: true }).nullable(),
  unreconciledSymbols: z.array(z.string()),
  notice: z.string(),
});

export type PortfolioPerformance = z.infer<typeof portfolioPerformanceSchema>;
