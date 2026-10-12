'use client';

import { PureComponent, type ReactNode } from 'react';
import { RequestService } from '../../../shared/services/RequestService';
import { HistoryService } from '../../../features/crypto/services/HistoryService';
import { PortfolioService } from '../../../features/crypto/services/PortfolioService';
import { CryptoServiceContext } from '../../../features/crypto/context/CryptoServiceContext';
import type { ICryptoServices } from '../../../features/crypto/context/ICryptoServices';
import type { ServiceProviderProps } from './ServiceProviderProps';

export default class ServiceProvider extends PureComponent<ServiceProviderProps> {
  private readonly services: ICryptoServices;

  public constructor(props: ServiceProviderProps) {
    super(props);
    this.services = props.services ?? ServiceProvider.createServices();
  }

  public override render(): ReactNode {
    return (
      <CryptoServiceContext.value.Provider value={this.services}>
        {this.props.children}
      </CryptoServiceContext.value.Provider>
    );
  }

  private static createServices(): ICryptoServices {
    const requests = new RequestService();

    return {
      history: new HistoryService(requests),
      portfolio: new PortfolioService(requests),
    };
  }
}
