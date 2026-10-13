import { z } from 'zod';

export enum OrderStatus {
  Pending = 'pending',
  Expired = 'expired',
  Rejected = 'rejected',
  Submitting = 'submitting',
  Submitted = 'submitted',
  Unknown = 'unknown',
}

export const orderStatusSchema = z.enum(OrderStatus);
