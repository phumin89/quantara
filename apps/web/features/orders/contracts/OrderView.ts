import { z } from 'zod';
import { orderSideSchema } from './OrderSide';
import { orderStatusSchema } from './OrderStatus';

export const orderViewSchema = z.object({
  id: z.uuid(),
  symbol: z.string().regex(/^[A-Z0-9]{1,30}$/),
  side: orderSideSchema,
  amount: z
    .string()
    .regex(/^\d+(?:\.\d+)?$/)
    .refine((value) => Number(value) > 0 && Number.isFinite(Number(value))),
  limitPrice: z
    .string()
    .regex(/^\d+(?:\.\d+)?$/)
    .refine((value) => Number(value) > 0 && Number.isFinite(Number(value))),
  notionalThb: z.string().regex(/^\d+(?:\.\d+)?$/),
  estimatedFeeThb: z.string().regex(/^\d+(?:\.\d+)?$/),
  referencePrice: z.string().regex(/^\d+(?:\.\d+)?$/),
  reason: z.string(),
  createdAt: z.iso.datetime({ offset: true }),
  expiresAt: z.iso.datetime({ offset: true }),
  status: orderStatusSchema,
  brokerOrderId: z.string().nullable(),
  message: z.string().nullable(),
  sizing: z
    .object({
      policyHash: z.string().regex(/^[a-f0-9]{64}$/),
      observedAt: z.iso.datetime({ offset: true }),
      equityThb: z.string().regex(/^\d+(?:\.\d+)?$/),
      allocationFraction: z.string().regex(/^\d+(?:\.\d+)?$/),
      estimatedDownsideThb: z.string().regex(/^\d+(?:\.\d+)?$/),
      estimatedCostThb: z.string().regex(/^\d+(?:\.\d+)?$/),
      projectedExposureThb: z.string().regex(/^\d+(?:\.\d+)?$/),
      code: z.string().min(1).max(64),
      explanation: z.string().min(1).max(1000),
    })
    .strict()
    .nullable()
    .optional(),
});

export type OrderView = z.infer<typeof orderViewSchema>;
