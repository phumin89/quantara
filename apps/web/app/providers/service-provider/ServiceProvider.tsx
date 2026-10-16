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
import { TradingServiceContext } from '../../../features/trading/context/TradingServiceContext';
import type { ITradingService } from '../../../features/trading/services/ITradingService';
import { TradingServices } from './TradingServices';
import { RiskServiceContext } from '../../../features/crypto/context/RiskServiceContext';
import type { IRiskService } from '../../../features/crypto/services/IRiskService';
import { RiskServices } from './RiskServices';

export default class ServiceProvider extends PureComponent<ServiceProviderProps> {
  private readonly services: ICryptoServices;

  private readonly teamService: ITeamService;

  private readonly ordersService: IOrdersService;

  private readonly tradingService: ITradingService;

  private readonly riskService: IRiskService;

  public constructor(props: ServiceProviderProps) {
    super(props);
    const container = new ServiceContainer<ServiceRegistry>(
      ServiceBindings.factories,
    );
    this.services = props.services ?? new CryptoServices(container);
    this.teamService = props.teamService ?? new TeamServices(container);
    this.ordersService = props.ordersService ?? new OrdersServices(container);
    this.tradingService =
      props.tradingService ?? new TradingServices(container);
    this.riskService = props.riskService ?? new RiskServices(container);
  }

  public override render(): ReactNode {
    return (
      <CryptoServiceContext.value.Provider value={this.services}>
        <TeamServiceContext.value.Provider value={this.teamService}>
          <OrdersServiceContext.value.Provider value={this.ordersService}>
            <TradingServiceContext.value.Provider value={this.tradingService}>
              <RiskServiceContext.value.Provider value={this.riskService}>
                {this.props.children}
              </RiskServiceContext.value.Provider>
            </TradingServiceContext.value.Provider>
          </OrdersServiceContext.value.Provider>
        </TeamServiceContext.value.Provider>
      </CryptoServiceContext.value.Provider>
    );
  }
}
