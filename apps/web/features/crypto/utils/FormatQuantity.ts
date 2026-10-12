export default class FormatQuantity {
  public static format(value: number): string {
    return value.toLocaleString('en-US', { maximumFractionDigits: 12 });
  }
}
