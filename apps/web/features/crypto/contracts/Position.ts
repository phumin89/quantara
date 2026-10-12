import { z } from 'zod';
import { CostStatus } from './CostStatus';
import { PositionStatus } from './PositionStatus';

export const positionSchema = z.object({
  symbol: z.string(),
  name: z.string(),
  status: z.enum(PositionStatus),
  quantity: z.number(),
  averageCost: z.number().nullable(),
  currentPrice: z.number().nullable(),
  value: z.number().nullable(),
  unrealizedPnl: z.number().nullable(),
  realizedPnl: z.number().nullable(),
  costStatus: z.literal(CostStatus.Unknown),
});

export type Position = z.infer<typeof positionSchema>;
