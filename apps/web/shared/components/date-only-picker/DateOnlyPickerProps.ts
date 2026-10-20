export interface DateOnlyPickerProps {
  readonly value: string;
  readonly label?: string;
  readonly maxDate?: string;
  readonly onChange: (value: string) => void;
}
