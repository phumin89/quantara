import { z } from 'zod';
import { TradeSide } from './TradeSide';

export const tradeSchema = z.object({
  transactionId: z.string(),
  symbol: z.string().regex(/^[a-zA-Z0-9]{1,30}$/),
  side: z.enum(TradeSide),
  quantity: z.number(),
  price: z.number(),
  fee: z.number(),
  feeCredit: z.number(),
  timestamp: z.iso.datetime({ offset: true }),
});

export type Trade = z.infer<typeof tradeSchema>;
