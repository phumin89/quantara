'use client';

import { Component, type ContextType } from 'react';
import { TeamServiceContext } from '../../context/TeamServiceContext';
import { TeamActivityContext } from '../../context/TeamActivityContext';
import { TeamActivitySnapshot } from '../../presentation/TeamActivitySnapshot';
import type { TeamActivityState } from '../../context/TeamActivityState';
import type { TeamActivityProviderProps } from './TeamActivityProviderProps';

export class TeamActivityProvider extends Component<
  TeamActivityProviderProps,
  TeamActivityState
> {
  public static override contextType = TeamServiceContext.value;

  declare public context: ContextType<typeof TeamServiceContext.value>;

  public override state: TeamActivityState = {
    date: '',
    overview: null,
    brief: null,
    loading: true,
    error: null,
    fetchedAt: null,
    now: 0,
    accountKey: null,
    scopeRevision: 0,
  };

  private mounted = false;

  private generation = 0;

  private controller: AbortController | null = null;

  private pending: Promise<void> | null = null;

  private timer: ReturnType<typeof setInterval> | null = null;

  public override componentDidMount(): void {
    this.mounted = true;
    document.addEventListener('visibilitychange', this.visibility);
    void this.refresh();
    this.timer = setInterval(() => {
      if (!document.hidden) {
        this.setState({ now: Date.now() });
        void this.refresh();
      }
    }, 10000);
  }

  public override componentWillUnmount(): void {
    this.mounted = false;
    this.cancel();
    if (this.timer) {
      clearInterval(this.timer);
    }

    document.removeEventListener('visibilitychange', this.visibility);
  }

  private cancel(): void {
    this.generation += 1;
    this.controller?.abort();
    this.controller = null;
    this.pending = null;
  }

  private readonly visibility = (): void => {
    if (document.hidden) {
      this.cancel();
    } else {
      this.setState({ now: Date.now() });
      void this.refresh();
    }
  };

  private readonly refresh = (): Promise<void> => {
    if (this.pending) {
      return this.pending;
    }

    const pending = this.load();
    this.pending = pending;
    void pending.finally(() => {
      if (this.pending === pending) {
        this.pending = null;
      }
    });

    return pending;
  };

  private async load(): Promise<void> {
    const controller = new AbortController();
    this.controller = controller;
    const generation = ++this.generation;
    const date = TeamActivitySnapshot.today();
    if (date !== this.state.date) {
      this.setState({
        date,
        brief: null,
        loading: true,
        fetchedAt: null,
        error: null,
      });
    }

    const timeout = setTimeout(() => {
      controller.abort();
    }, 8000);
    try {
      const service = TeamServiceContext.require(this.context);
      const [overviewResult, briefResult] = await Promise.allSettled([
        service.get(controller.signal),
        service.brief(date, controller.signal),
      ]);
      if (!this.mounted || generation !== this.generation) {
        return;
      }

      if (overviewResult.status === 'rejected') {
        throw overviewResult.reason instanceof Error
          ? overviewResult.reason
          : new Error('Unable to load research status.');
      }

      const overview = overviewResult.value;
      if (TeamActivitySnapshot.isOlder(this.state.overview, overview)) {
        throw new Error(
          'An older team snapshot was received. Showing the last verified records.',
        );
      }

      const accountKey = overview.latestRun?.accountKey ?? null;
      const changed =
        accountKey !== this.state.accountKey ||
        Boolean(this.state.overview?.latestRun && !overview.latestRun);
      const scopeRevision = this.state.scopeRevision + (changed ? 1 : 0);
      if (changed) {
        this.setState({
          overview,
          brief: null,
          accountKey,
          scopeRevision,
          fetchedAt: null,
        });
      }

      if (controller.signal.aborted) {
        throw new Error('Research status request timed out.');
      }

      if (briefResult.status === 'rejected') {
        throw briefResult.reason instanceof Error
          ? briefResult.reason
          : new Error('Unable to load research reports.');
      }

      const brief = briefResult.value;
      this.setState({
        date,
        overview,
        brief: TeamActivitySnapshot.scope(overview, brief),
        loading: false,
        error: null,
        fetchedAt: Date.now(),
        now: Date.now(),
        accountKey,
        scopeRevision,
      });
    } catch (cause) {
      if (this.mounted && generation === this.generation && !document.hidden) {
        let error = 'Unable to load research status.';
        if (controller.signal.aborted) {
          error = 'Research status request timed out.';
        } else if (cause instanceof Error) {
          error = cause.message
            .replace(/[\u0000-\u001f\u007f]/g, ' ')
            .slice(0, 700);
        }

        this.setState({
          loading: false,
          now: Date.now(),
          error,
        });
      }
    } finally {
      clearTimeout(timeout);
      if (this.controller === controller) {
        this.controller = null;
      }
    }
  }

  public override render() {
    return (
      <TeamActivityContext.value.Provider
        value={{
          ...this.state,
          service: TeamServiceContext.require(this.context),
          refresh: this.refresh,
        }}
      >
        {this.props.children}
      </TeamActivityContext.value.Provider>
    );
  }
}
