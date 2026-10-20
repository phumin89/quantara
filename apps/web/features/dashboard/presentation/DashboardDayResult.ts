export interface DashboardDayResult {
  readonly realizedNet: number | null;
  readonly filledOrders: number | null;
  readonly closedTrades: number | null;
  readonly recordedAt: string | null;
  readonly source: string;
}
