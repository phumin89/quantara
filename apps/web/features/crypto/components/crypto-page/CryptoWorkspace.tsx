'use client';

import { Component, type ContextType } from 'react';
import { TeamActivityContext } from '../../../team/context/TeamActivityContext';
import CryptoPage from './CryptoPage';

export default class CryptoWorkspace extends Component {
  public static override contextType = TeamActivityContext.value;

  declare public context: ContextType<typeof TeamActivityContext.value>;

  public override render() {
    const activity = TeamActivityContext.require(this.context);

    return (
      <CryptoPage
        key={
          String(activity.scopeRevision) +
          ':' +
          (activity.accountKey ?? 'unconfirmed')
        }
      />
    );
  }
}
