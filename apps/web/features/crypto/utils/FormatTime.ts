export default class FormatTime {
  public static format(value: string | null): string {
    return value
      ? new Date(value).toLocaleString('en-US', {
          dateStyle: 'medium',
          timeStyle: 'short',
          timeZone: 'Asia/Bangkok',
          hour12: false,
        })
      : '—';
  }
}
