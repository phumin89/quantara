export default class FormatMoney {
  public static format(value: number | null): string {
    return value == null
      ? '—'
      : `฿${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }
}
