'use client';

import { PureComponent, type ReactNode } from 'react';
import { CryptoServiceContext } from '../../../features/crypto/context/CryptoServiceContext';
import type { ICryptoServices } from '../../../features/crypto/context/ICryptoServices';
import { ServiceContainer } from '../../../shared/di/ServiceContainer';
import type { ServiceProviderProps } from './ServiceProviderProps';
import { CryptoServices } from './CryptoServices';
import { ServiceBindings } from './ServiceBindings';
import type { ServiceRegistry } from './ServiceRegistry';

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
    return new CryptoServices(
      new ServiceContainer<ServiceRegistry>(ServiceBindings.factories),
    );
  }
}
