import { createContext } from 'react';
import type { IRiskService } from '../services/IRiskService';

export class RiskServiceContext {
  public static readonly value = createContext<IRiskService | null>(null);

  public static require(value: IRiskService | null): IRiskService {
    if (value === null) {
      throw new Error('Risk service provider is required.');
    }

    return value;
  }
}
