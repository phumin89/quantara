export default class FormatMoney {
  public static format(value: number | null): string {
    return value == null
      ? '—'
      : `฿${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }

  public static pnl(value: number | null): string {
    return value == null
      ? '—'
      : new Intl.NumberFormat('en-US', {
          style: 'currency',
          currency: 'THB',
          currencyDisplay: 'narrowSymbol',
          signDisplay: 'exceptZero',
          minimumFractionDigits: 2,
          maximumFractionDigits: 2,
        }).format(value);
  }
}
