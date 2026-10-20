export interface DashboardIssue {
  readonly key: string;
  readonly severity: 'info' | 'warning' | 'error';
  readonly title: string;
  readonly href: string;
  readonly action: string;
}
