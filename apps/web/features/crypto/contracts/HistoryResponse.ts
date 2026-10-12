import { z } from "zod";
import { tradeSchema } from "./Trade";

export const historyResponseSchema = z.object({
  trades: z.array(tradeSchema),
  coverage: z.string().optional(),
});

export type HistoryResponse = z.infer<typeof historyResponseSchema>;
