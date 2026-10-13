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
import { OrdersServiceContext } from '../../../features/orders/context/OrdersServiceContext';
import type { IOrdersService } from '../../../features/orders/services/IOrdersService';
import { OrdersServices } from './OrdersServices';

export default class ServiceProvider extends PureComponent<ServiceProviderProps> {
  private readonly services: ICryptoServices;

  private readonly teamService: ITeamService;

  private readonly ordersService: IOrdersService;

  public constructor(props: ServiceProviderProps) {
    super(props);
    const container = new ServiceContainer<ServiceRegistry>(
      ServiceBindings.factories,
    );
    this.services = props.services ?? new CryptoServices(container);
    this.teamService = props.teamService ?? new TeamServices(container);
    this.ordersService = props.ordersService ?? new OrdersServices(container);
  }

  public override render(): ReactNode {
    return (
      <CryptoServiceContext.value.Provider value={this.services}>
        <TeamServiceContext.value.Provider value={this.teamService}>
          <OrdersServiceContext.value.Provider value={this.ordersService}>
            {this.props.children}
          </OrdersServiceContext.value.Provider>
        </TeamServiceContext.value.Provider>
      </CryptoServiceContext.value.Provider>
    );
  }
}
