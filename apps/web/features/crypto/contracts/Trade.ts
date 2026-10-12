import { z } from "zod";

export const tradeSchema = z.object({
  transactionId: z.string(),
  symbol: z.string(),
  side: z.enum(["buy", "sell"]),
  quantity: z.number(),
  price: z.number(),
  fee: z.number(),
  feeCredit: z.number(),
  timestamp: z.string(),
});

export type Trade = z.infer<typeof tradeSchema>;
