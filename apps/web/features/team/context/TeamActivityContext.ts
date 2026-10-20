'use client';

import { createContext } from 'react';
import type { TeamActivityValue } from './TeamActivityValue';

export class TeamActivityContext {
  public static readonly value = createContext<TeamActivityValue | null>(null);

  public static require(value: TeamActivityValue | null): TeamActivityValue {
    if (value === null) {
      throw new Error('Team activity provider is required.');
    }

    return value;
  }
}
