import { z } from 'zod';
import { orderViewSchema } from './OrderView';

export const orderResponseSchema = z.object({
  proposals: z.array(orderViewSchema),
});

export type OrderResponse = z.infer<typeof orderResponseSchema>;
