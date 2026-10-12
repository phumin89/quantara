import type { ReactNode } from 'react';
import type { ICryptoServices } from '../../../features/crypto/context/ICryptoServices';

export interface ServiceProviderProps {
  readonly children: ReactNode;
  readonly services?: ICryptoServices;
}
