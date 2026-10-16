'use client';

import { createContext } from 'react';
import type { ITradingService } from '../services/ITradingService';

export class TradingServiceContext {
  public static readonly value = createContext<ITradingService | null>(null);

  public static require(value: ITradingService | null): ITradingService {
    if (value === null) {
      throw new Error('Trading service provider is required.');
    }

    return value;
  }
}
