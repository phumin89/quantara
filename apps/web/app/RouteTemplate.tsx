'use client';

import { Component, type ReactNode } from 'react';
import type { RootLayoutProps } from './RootLayoutProps';

export default class RouteTemplate extends Component<RootLayoutProps> {
  public override componentDidMount(): void {
    window.dispatchEvent(new Event('quantara:route-change'));
  }

  public override render(): ReactNode {
    return this.props.children;
  }
}
