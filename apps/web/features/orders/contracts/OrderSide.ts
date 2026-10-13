import { z } from 'zod';

export enum OrderSide {
  Buy = 'buy',
  Sell = 'sell',
}

export const orderSideSchema = z.enum(OrderSide);
