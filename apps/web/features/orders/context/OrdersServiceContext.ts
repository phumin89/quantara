'use client';

import { createContext } from 'react';
import type { IOrdersService } from '../services/IOrdersService';

export class OrdersServiceContext {
  public static readonly value = createContext<IOrdersService | null>(null);

  public static require(value: IOrdersService | null): IOrdersService {
    if (value === null) {
      throw new Error('Orders service provider is required.');
    }

    return value;
  }
}
