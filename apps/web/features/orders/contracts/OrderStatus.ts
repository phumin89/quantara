import { z } from 'zod';

export enum OrderStatus {
  Pending = 'pending',
  Expired = 'expired',
  Rejected = 'rejected',
  Submitting = 'submitting',
  Submitted = 'submitted',
  Unknown = 'unknown',
  PartiallyFilled = 'partiallyfilled',
  Filled = 'filled',
  Cancelled = 'cancelled',
}

export const orderStatusSchema = z.enum(OrderStatus);
