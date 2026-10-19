export default class TradingFormatting {
  public static money(value: string | null): string {
    if (value === null) {
      return 'Unknown';
    }

    return `฿${Number(value).toLocaleString('en-GB', { maximumFractionDigits: 2 })}`;
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
