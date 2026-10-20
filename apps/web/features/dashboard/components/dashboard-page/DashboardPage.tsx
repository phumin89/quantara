'use client';

import { Component, type ReactNode } from 'react';
import { CryptoServiceContext } from '../../../crypto/context/CryptoServiceContext';
import { TradingServiceContext } from '../../../trading/context/TradingServiceContext';
import { TeamActivityContext } from '../../../team/context/TeamActivityContext';
import DashboardScreen from '../dashboard-screen/DashboardScreen';
import { DashboardServiceContext } from '../../context/DashboardServiceContext';

export default class DashboardPage extends Component {
  public override render(): ReactNode {
    return (
      <CryptoServiceContext.value.Consumer>
        {(crypto) => (
          <TradingServiceContext.value.Consumer>
            {(trading) => (
              <TeamActivityContext.value.Consumer>
                {(activity) => (
                  <DashboardServiceContext.value.Provider
                    value={{
                      portfolio: CryptoServiceContext.require(crypto).portfolio,
                      trading: TradingServiceContext.require(trading),
                    }}
                  >
                    <DashboardScreen
                      activity={TeamActivityContext.require(activity)}
                    />
                  </DashboardServiceContext.value.Provider>
                )}
              </TeamActivityContext.value.Consumer>
            )}
          </TradingServiceContext.value.Consumer>
        )}
      </CryptoServiceContext.value.Consumer>
    );
  }
}
