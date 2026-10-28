'use client';

import { Component, type ReactNode, type ContextType } from 'react';
import { RiskServiceContext } from '../../../crypto/context/RiskServiceContext';
import { TradingServiceContext } from '../../../trading/context/TradingServiceContext';
import { TeamActivityContext } from '../../../team/context/TeamActivityContext';
import DashboardScreen from '../dashboard-screen/DashboardScreen';
import { DashboardServiceContext } from '../../context/DashboardServiceContext';

export default class DashboardPage extends Component {
  public static override contextType = RiskServiceContext.value;

  declare public context: ContextType<typeof RiskServiceContext.value>;

  public override render(): ReactNode {
    return (
      <TradingServiceContext.value.Consumer>
        {(trading) => (
          <TeamActivityContext.value.Consumer>
            {(activity) => (
              <DashboardServiceContext.value.Provider
                value={{
                  trading: TradingServiceContext.require(trading),
                  risk: RiskServiceContext.require(this.context),
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
    );
  }
}
