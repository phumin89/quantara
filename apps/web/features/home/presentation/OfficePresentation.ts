import { OfficeAgentStatus } from '../contracts/OfficeAgentStatus';
import type { CompanyLoopOverview } from '../../team/contracts/CompanyLoopOverview';
import { CompanyLoopStatus } from '../../team/contracts/CompanyLoopStatus';

export class OfficePresentation {
  public static color(
    status: OfficeAgentStatus,
  ): 'success' | 'error' | 'warning' | 'info' | 'default' {
    if (
      status === OfficeAgentStatus.Active ||
      status === OfficeAgentStatus.Completed
    ) {
      return 'success';
    }

    if (status === OfficeAgentStatus.Failed) {
      return 'error';
    }

    if (
      [
        OfficeAgentStatus.Blocked,
        OfficeAgentStatus.Stale,
        OfficeAgentStatus.Unknown,
      ].includes(status)
    ) {
      return 'warning';
    }

    if (status === OfficeAgentStatus.Queued) {
      return 'info';
    }

    return 'default';
  }

  public static label(status: OfficeAgentStatus): string {
    return status === OfficeAgentStatus.Active
      ? 'Working'
      : status.charAt(0).toUpperCase() + status.slice(1);
  }

  public static company(
    company: CompanyLoopOverview | null | undefined,
  ): string {
    if (!company) {
      return 'Research status is unavailable.';
    }

    if (!company.enabled) {
      return 'Automatic research is stopped.';
    }

    if (company.error) {
      return 'Research needs attention. The last recorded error is retained below.';
    }

    if (company.budgetReason === 'waiting_for_evidence') {
      return 'Waiting for fresh evidence before the next assessment.';
    }

    if (company.status === CompanyLoopStatus.Running) {
      return 'Research is in progress. Reports remain read-only.';
    }

    if (company.status === CompanyLoopStatus.WaitingForCodex) {
      return 'Waiting for Codex configuration.';
    }

    if (company.status === CompanyLoopStatus.WaitingForPortfolio) {
      return 'Waiting for verified portfolio evidence.';
    }

    if (company.status === CompanyLoopStatus.DeferredBudget) {
      return 'Research is deferred until the next budget window.';
    }

    return 'Research is enabled and waiting for its next assessment.';
  }

  public static time(at: string | number | null | undefined): string {
    if (at === null || at === undefined) {
      return 'Not recorded';
    }

    return (
      new Intl.DateTimeFormat('en-GB', {
        timeZone: 'Asia/Bangkok',
        day: '2-digit',
        month: 'short',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      }).format(new Date(at)) + ' ICT'
    );
  }
}
