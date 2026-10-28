import type { DashboardChartPoint } from '../../presentation/DashboardChartPoint';

export interface DashboardChartProps {
  readonly title: string;
  readonly points: readonly DashboardChartPoint[];
  readonly kind: 'line' | 'bar';
  readonly source: string;
  readonly empty: string;
}
