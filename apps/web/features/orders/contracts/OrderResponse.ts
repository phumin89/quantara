import { z } from 'zod';
import { orderViewSchema } from './OrderView';

export const orderResponseSchema = z
  .object({
    proposals: z.array(orderViewSchema),
  })
  .refine(
    (response) =>
      new Set(response.proposals.map((proposal) => proposal.id.toLowerCase()))
        .size === response.proposals.length,
    { message: 'Order proposal IDs must be unique.' },
  );

export type OrderResponse = z.infer<typeof orderResponseSchema>;
