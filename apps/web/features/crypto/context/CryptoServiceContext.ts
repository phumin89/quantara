'use client';

import { createContext } from 'react';
import type { ICryptoServices } from './ICryptoServices';

export class CryptoServiceContext {
  public static readonly value = createContext<ICryptoServices | null>(null);

  public static require(value: ICryptoServices | null): ICryptoServices {
    if (value === null) {
      throw new Error('Crypto services provider is required.');
    }

    return value;
  }
}
