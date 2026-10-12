'use client';

import { PureComponent } from 'react';
import { Typography } from '@mui/material';

export default class HomePage extends PureComponent {
  public override render() {
    return (
      <Typography
        component="h1"
        variant="h1"
        sx={{ m: 0 }}
      >
        Home
      </Typography>
    );
  }
}
