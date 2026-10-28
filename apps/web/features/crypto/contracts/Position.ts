import { z } from 'zod';
import { CostStatus } from './CostStatus';
import { PositionStatus } from './PositionStatus';

export const positionSchema = z
  .object({
    symbol: z.string().regex(/^[A-Za-z0-9]{1,30}$/),
    name: z.string(),
    status: z.enum(PositionStatus),
    quantity: z.number(),
    averageCost: z.number().nullable(),
    currentPrice: z.number().nullable(),
    value: z.number().nullable(),
    unrealizedPnl: z.number().nullable(),
    realizedPnl: z.number().nullable(),
    costStatus: z.enum(CostStatus),
    lastBuyPrice: z.number().positive().nullable().optional(),
    lastBoughtAt: z.iso.datetime({ offset: true }).nullable().optional(),
  })
  .refine(
    (position) =>
      position.costStatus !== CostStatus.Unknown ||
      position.averageCost === null,
    { message: 'Unknown position cost cannot include an average cost.' },
  );

export type Position = z.infer<typeof positionSchema>;
