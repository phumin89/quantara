'use client';

import { Component } from 'react';
import { Box } from '@mui/material';
import { DatePicker } from '@mui/x-date-pickers/DatePicker';
import { BangkokDate } from '../../presentation/BangkokDate';
import type { DateOnlyPickerProps } from './DateOnlyPickerProps';
import type { DateOnlyPickerState } from './DateOnlyPickerState';

export class DateOnlyPicker extends Component<
  DateOnlyPickerProps,
  DateOnlyPickerState
> {
  public override state: DateOnlyPickerState = {
    value: BangkokDate.parse(this.props.value),
    error: null,
  };

  public override componentDidUpdate(previous: DateOnlyPickerProps): void {
    if (previous.value !== this.props.value) {
      this.setState({
        value: BangkokDate.parse(this.props.value),
        error: null,
      });
    }
  }

  public override render() {
    const minimum = BangkokDate.parse('1970-01-01');
    const maximum = this.props.maxDate
      ? BangkokDate.parse(this.props.maxDate)
      : null;

    return (
      <Box
        data-date={this.props.value}
        sx={{ width: { xs: '100%', sm: 210 }, maxWidth: 260 }}
      >
        <DatePicker
          label={this.props.label ?? 'Date'}
          value={this.state.value}
          timezone="Asia/Bangkok"
          format="DD/MM/YYYY"
          {...(minimum ? { minDate: minimum } : {})}
          {...(maximum ? { maxDate: maximum } : {})}
          onChange={(value) => {
            this.setState({ value });
          }}
          onAccept={(value, context) => {
            const date = BangkokDate.format(value);
            if (date && !context.validationError) {
              this.props.onChange(date);
            }
          }}
          onError={(reason) => {
            let error: string | null = null;
            if (reason) {
              error =
                reason === 'maxDate'
                  ? 'Choose an earlier date.'
                  : 'Enter a valid date.';
            }

            this.setState({ error });
          }}
          slotProps={{
            textField: {
              size: 'small',
              fullWidth: true,
              helperText: this.state.error,
              error: this.state.error !== null,
            },
            actionBar: { actions: ['cancel', 'accept'] },
          }}
        />
      </Box>
    );
  }
}
