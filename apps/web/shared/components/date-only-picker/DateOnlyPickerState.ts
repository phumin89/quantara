import type { Dayjs } from 'dayjs';

export interface DateOnlyPickerState {
  readonly value: Dayjs | null;
  readonly error: string | null;
}
