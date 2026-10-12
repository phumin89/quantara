import type { ReactNode } from 'react';
import type { ICryptoServices } from '../../../features/crypto/context/ICryptoServices';
import type { ITeamService } from '../../../features/team/services/ITeamService';

export interface ServiceProviderProps {
  readonly children: ReactNode;
  readonly services?: ICryptoServices;
  readonly teamService?: ITeamService;
}
