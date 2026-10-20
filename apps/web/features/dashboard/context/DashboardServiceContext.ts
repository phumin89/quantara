'use client';

import { createContext } from 'react';
import type { IDashboardServices } from './IDashboardServices';

export class DashboardServiceContext {
  public static readonly value = createContext<IDashboardServices | null>(null);

  public static require(value: IDashboardServices | null): IDashboardServices {
    if (value === null) {
      throw new Error('Dashboard services provider is required.');
    }

    return value;
  }
}
