'use client';

import { createContext } from 'react';
import type { ITeamService } from '../services/ITeamService';

export class TeamServiceContext {
  public static readonly value = createContext<ITeamService | null>(null);

  public static require(value: ITeamService | null): ITeamService {
    if (value === null) {
      throw new Error('Team service provider is required.');
    }

    return value;
  }
}
