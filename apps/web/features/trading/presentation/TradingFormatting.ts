export default class TradingFormatting {
  private static readonly moneyFormat = new Intl.NumberFormat('en-GB', {
    maximumFractionDigits: 2,
  });

  public static money(value: string | null): string {
    if (value === null || !TradingFormatting.numeric(value)) {
      return 'Unknown';
    }

    return `฿${TradingFormatting.moneyFormat.format(value)}`;
  }

  private static numeric(value: string): value is Intl.StringNumericLiteral {
    return value.trim().length > 0 && Number.isFinite(Number(value));
  }

  public static sum(values: readonly string[]): string | null {
    let scale = 0;
    for (const value of values) {
      if (
        !/^-?\d+(?:\.\d+)?$/.test(value) ||
        !TradingFormatting.numeric(value)
      ) {
        return null;
      }

      scale = Math.max(scale, value.split('.')[1]?.length ?? 0);
    }

    let total = BigInt(0);
    for (const value of values) {
      const [whole = '0', fraction = ''] = value.split('.');
      total += BigInt(whole + fraction.padEnd(scale, '0'));
    }

    return `${total.toString()}e-${String(scale)}`;
  }

  public static moneyNumber(value: number): string {
    return `฿${value.toLocaleString('en-GB', { maximumFractionDigits: 2 })}`;
  }

  public static percent(value: number | undefined): string {
    return value === undefined
      ? 'Unknown'
      : `${(value * 100).toLocaleString('en-GB', { maximumFractionDigits: 2 })}%`;
  }

  public static localDateTime(timestamp: number): string {
    const date = new Date(timestamp);

    return new Date(timestamp - date.getTimezoneOffset() * 60_000)
      .toISOString()
      .slice(0, 16);
  }

  public static time(value: string | null): string {
    if (value === null) {
      return 'Unavailable';
    }

    return `${new Date(value).toLocaleString('en-GB', { timeZone: 'Asia/Bangkok' })} Bangkok`;
  }

  public static age(value: string): string {
    const ageMs = Math.max(0, Date.now() - Date.parse(value));
    if (ageMs < 60_000) {
      return `${Math.floor(ageMs / 1000).toString()}s ago`;
    }

    return `${Math.floor(ageMs / 60_000).toString()}m ago`;
  }

  public static timeWithAge(value: string | null): string {
    return value === null
      ? 'Unknown'
      : `${TradingFormatting.time(value)} (${TradingFormatting.age(value)})`;
  }

  public static milliseconds(value: number | null): string {
    return value === null ? 'Unknown' : `${value.toLocaleString('en-GB')} ms`;
  }
}
