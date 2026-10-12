'use client';

import { PureComponent, type ReactNode } from 'react';
import { CryptoServiceContext } from '../../../features/crypto/context/CryptoServiceContext';
import type { ICryptoServices } from '../../../features/crypto/context/ICryptoServices';
import type { ITeamService } from '../../../features/team/services/ITeamService';
import { TeamServiceContext } from '../../../features/team/context/TeamServiceContext';
import { TeamServices } from './TeamServices';
import { ServiceContainer } from '../../../shared/di/ServiceContainer';
import type { ServiceProviderProps } from './ServiceProviderProps';
import { CryptoServices } from './CryptoServices';
import { ServiceBindings } from './ServiceBindings';
import type { ServiceRegistry } from './ServiceRegistry';

export default class ServiceProvider extends PureComponent<ServiceProviderProps> {
  private readonly services: ICryptoServices;

  private readonly teamService: ITeamService;

  public constructor(props: ServiceProviderProps) {
    super(props);
    const container = new ServiceContainer<ServiceRegistry>(
      ServiceBindings.factories,
    );
    this.services = props.services ?? new CryptoServices(container);
    this.teamService = props.teamService ?? new TeamServices(container);
  }

  public override render(): ReactNode {
    return (
      <CryptoServiceContext.value.Provider value={this.services}>
        <TeamServiceContext.value.Provider value={this.teamService}>
          {this.props.children}
        </TeamServiceContext.value.Provider>
      </CryptoServiceContext.value.Provider>
    );
  }
}
