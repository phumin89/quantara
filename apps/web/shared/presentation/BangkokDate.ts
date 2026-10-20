import dayjs, { type Dayjs } from 'dayjs';
import customParseFormat from 'dayjs/plugin/customParseFormat';
import utc from 'dayjs/plugin/utc';
import timezone from 'dayjs/plugin/timezone';

dayjs.extend(customParseFormat);
dayjs.extend(utc);
dayjs.extend(timezone);

export class BangkokDate {
  public static parse(value: string): Dayjs | null {
    if (
      !/^\d{4}-\d{2}-\d{2}$/.test(value) ||
      !dayjs(value, 'YYYY-MM-DD', true).isValid()
    ) {
      return null;
    }

    return dayjs.tz(`${value}T12:00:00`, 'Asia/Bangkok');
  }

  public static format(value: Dayjs | null): string | null {
    return value?.isValid()
      ? value.tz('Asia/Bangkok').format('YYYY-MM-DD')
      : null;
  }
}
