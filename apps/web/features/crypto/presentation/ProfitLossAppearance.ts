export default class ProfitLossAppearance {
  public static color(value: number | null): string {
    if (value === null) {
      return 'text.secondary';
    }

    if (value < 0) {
      return 'error.main';
    }

    return value > 0 ? 'success.main' : 'text.primary';
  }
}
