'use client';

import { Component, createRef } from 'react';
import { Box, Typography } from '@mui/material';
import type { OfficeCanvasProps } from './OfficeCanvasProps';
import type { OfficeCanvasState } from './OfficeCanvasState';
import type { PixelOfficeBridge } from '../../office/PixelOfficeBridge';
import type { PixelOfficeModule } from '../../office/PixelOfficeModule';
import { OfficeAgentStatus } from '../../contracts/OfficeAgentStatus';

export class OfficeCanvas extends Component<
  OfficeCanvasProps,
  OfficeCanvasState
> {
  public override state: OfficeCanvasState = { error: false, ready: false };

  private readonly canvas = createRef<HTMLCanvasElement>();

  private bridge: PixelOfficeBridge | null = null;

  private resizeObserver: ResizeObserver | null = null;

  private intersectionObserver: IntersectionObserver | null = null;

  private media: MediaQueryList | null = null;

  private frame = 0;

  private lastFrame = 0;

  private mounted = false;

  private visible = true;

  public override componentDidMount(): void {
    this.mounted = true;
    this.media = window.matchMedia('(prefers-reduced-motion: reduce)');
    this.media.addEventListener('change', this.updateAnimation);
    document.addEventListener('visibilitychange', this.updateAnimation);
    void this.initialize();
  }

  public override componentDidUpdate(): void {
    this.sync();
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    cancelAnimationFrame(this.frame);
    this.resizeObserver?.disconnect();
    this.intersectionObserver?.disconnect();
    this.media?.removeEventListener('change', this.updateAnimation);
    document.removeEventListener('visibilitychange', this.updateAnimation);
    this.bridge = null;
  }

  private async initialize(): Promise<void> {
    try {
      const source = '/vendor/pixel-agents/renderer.js';
      const module: unknown = await import(
        /* webpackIgnore: true */ /* turbopackIgnore: true */ source
      );
      if (
        !module ||
        typeof module !== 'object' ||
        !('PixelAgentsOffice' in module)
      ) {
        throw new Error('Renderer unavailable.');
      }

      const canvas = this.canvas.current;
      if (!canvas || !this.mounted) {
        return;
      }

      const bridge = await (
        module as PixelOfficeModule
      ).PixelAgentsOffice.create(canvas);
      if (!this.isAttached(canvas)) {
        return;
      }

      this.bridge = bridge;
      this.resizeObserver = new ResizeObserver(() => {
        this.resize();
      });
      this.resizeObserver.observe(canvas);
      this.intersectionObserver = new IntersectionObserver((entries) => {
        this.visible = entries.some((entry) => entry.isIntersecting);
        this.updateAnimation();
      });
      this.intersectionObserver.observe(canvas);
      this.resize();
      this.setState({ ready: true });
    } catch {
      if (this.mounted) {
        this.setState({ error: true });
      }
    }
  }

  private isAttached(canvas: HTMLCanvasElement): boolean {
    return this.mounted && this.canvas.current === canvas;
  }

  private resize(): void {
    const canvas = this.canvas.current;
    if (!canvas) {
      return;
    }

    const bounds = canvas.getBoundingClientRect();
    const ratio = Math.min(2, window.devicePixelRatio || 1);
    // The engine's one-pixel sprites require zoom >= 1; CSS can scale down the room.
    canvas.width = Math.max(400, Math.round(bounds.width * ratio));
    canvas.height = Math.max(272, Math.round(bounds.height * ratio));
    this.sync();
  }

  private sync(): void {
    this.bridge?.sync(
      this.props.agents.map((agent) => ({
        id: agent.id,
        active: agent.status === OfficeAgentStatus.Active,
      })),
    );
    this.bridge?.select(this.props.selectedId);
    this.updateAnimation();
  }

  private readonly updateAnimation = (): void => {
    cancelAnimationFrame(this.frame);
    this.lastFrame = 0;
    this.bridge?.draw();
    if (
      this.mounted &&
      this.bridge &&
      this.visible &&
      !document.hidden &&
      !this.media?.matches &&
      !this.props.paused &&
      this.props.agents.some(
        (agent) => agent.status === OfficeAgentStatus.Active,
      )
    ) {
      this.frame = requestAnimationFrame(this.animate);
    }
  };

  private readonly animate = (at: number): void => {
    if (!this.mounted || !this.bridge) {
      return;
    }

    this.bridge.advance(this.lastFrame ? (at - this.lastFrame) / 1000 : 0);
    this.lastFrame = at;
    this.bridge.draw();
    this.frame = requestAnimationFrame(this.animate);
  };

  public override render() {
    return (
      <Box sx={{ position: 'relative', bgcolor: '#f3f3f3', minWidth: 0 }}>
        <canvas
          ref={this.canvas}
          aria-label="Research team office. Select a role from the team list for recorded details."
          role="img"
          data-ready={this.state.ready}
          onClick={(event) => {
            const bounds = event.currentTarget.getBoundingClientRect();
            const id = this.bridge?.hitTest(
              ((event.clientX - bounds.left) * event.currentTarget.width) /
                bounds.width,
              ((event.clientY - bounds.top) * event.currentTarget.height) /
                bounds.height,
            );
            if (id) {
              this.props.onSelect(id);
            }
          }}
          style={{
            display: 'block',
            width: '100%',
            height: '100%',
            aspectRatio: '3 / 2',
            maxHeight: 440,
          }}
        >
          Recorded role status is available in the team list below.
        </canvas>
        {(!this.state.ready || this.state.error) && (
          <Typography
            role="status"
            sx={{ p: 2 }}
          >
            {this.state.error
              ? 'Office illustration unavailable. Recorded team status is still available below.'
              : 'Loading team office…'}
          </Typography>
        )}
      </Box>
    );
  }
}
