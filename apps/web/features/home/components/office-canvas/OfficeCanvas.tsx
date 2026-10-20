'use client';

import { Component, createRef } from 'react';
import { Box, Typography } from '@mui/material';
import type { OfficeCanvasProps } from './OfficeCanvasProps';
import type { OfficeCanvasState } from './OfficeCanvasState';
import { OfficeAgentStatus } from '../../contracts/OfficeAgentStatus';

export class OfficeCanvas extends Component<
  OfficeCanvasProps,
  OfficeCanvasState
> {
  public override state: OfficeCanvasState = { error: false, ready: false };

  private readonly frame = createRef<HTMLIFrameElement>();

  private observer: IntersectionObserver | null = null;

  private media: MediaQueryList | null = null;

  private timeout: ReturnType<typeof setTimeout> | null = null;

  private visible = true;

  public override componentDidMount(): void {
    window.addEventListener('message', this.onMessage);
    document.addEventListener('visibilitychange', this.sync);
    this.media = matchMedia('(prefers-reduced-motion: reduce)');
    this.media.addEventListener('change', this.sync);
    this.observer = new IntersectionObserver((entries) => {
      this.visible = entries.some((entry) => entry.isIntersecting);
      this.sync();
    });
    if (this.frame.current) {
      this.observer.observe(this.frame.current);
    }

    // SSR may load the child before hydration registers this message listener.
    this.frame.current?.contentWindow?.postMessage(
      { source: 'quantara-home', type: 'request-ready' },
      location.origin,
    );

    this.timeout = setTimeout(() => {
      if (!this.state.ready) {
        this.setState({ error: true });
      }
    }, 10000);
  }

  public override componentDidUpdate(): void {
    this.sync();
  }

  public override componentWillUnmount(): void {
    window.removeEventListener('message', this.onMessage);
    document.removeEventListener('visibilitychange', this.sync);
    this.media?.removeEventListener('change', this.sync);
    this.observer?.disconnect();
    if (this.timeout) {
      clearTimeout(this.timeout);
    }
  }

  private readonly onMessage = (event: MessageEvent): void => {
    if (
      event.source !== this.frame.current?.contentWindow ||
      event.origin !== location.origin
    ) {
      return;
    }

    const data: unknown = event.data;
    if (
      !data ||
      typeof data !== 'object' ||
      !('source' in data) ||
      data.source !== 'pixel-agents-office' ||
      !('type' in data)
    ) {
      return;
    }

    if (data.type === 'ready') {
      this.setState({ ready: true, error: false });
      this.sync();
    }

    if (
      data.type === 'select' &&
      'id' in data &&
      typeof data.id === 'number' &&
      this.props.agents.some((agent) => agent.id === data.id)
    ) {
      this.props.onSelect(data.id);
    }
  };

  private readonly sync = (): void => {
    this.frame.current?.contentWindow?.postMessage(
      {
        source: 'quantara-home',
        type: 'snapshot',
        agents: this.props.agents.map(({ id, label, status }) => ({
          id,
          label,
          status,
        })),
        selectedId: this.props.selectedId,
        paused:
          this.props.paused ||
          !this.visible ||
          document.hidden ||
          this.media?.matches === true ||
          !this.props.agents.some(
            (agent) => agent.status === OfficeAgentStatus.Active,
          ),
      },
      location.origin,
    );
  };

  public override render() {
    return (
      <Box sx={{ position: 'relative', minWidth: 0, bgcolor: '#1e1e2e' }}>
        <Box
          component="iframe"
          ref={this.frame}
          title="Pixel Agents research office"
          src="/vendor/pixel-agents/office/index.html"
          sandbox="allow-scripts allow-same-origin"
          referrerPolicy="no-referrer"
          data-ready={this.state.ready}
          sx={{
            display: 'block',
            width: '100%',
            height: { xs: 480, sm: 600, lg: 680 },
            border: 0,
          }}
        />
        {(!this.state.ready || this.state.error) && (
          <Typography
            role="status"
            sx={{ p: 2, color: '#fff' }}
          >
            {this.state.error
              ? 'Office unavailable. Team status is available below.'
              : 'Loading office…'}
          </Typography>
        )}
      </Box>
    );
  }
}
