import { Box, Typography } from '@mui/material';
import { Component, type ReactNode } from 'react';
import type { BriefMessageProps } from './BriefMessageProps';

export default class BriefMessage extends Component<BriefMessageProps> {
  public override render(): ReactNode {
    const lines = this.props.output
      .split(/\r?\n/)
      .filter((line) => line.trim().length > 0);
    const isShort = lines.length <= 1 && this.props.output.length <= 80;

    if (isShort) {
      return (
        <Typography
          variant="body2"
          sx={{
            whiteSpace: 'pre-wrap',
            overflowWrap: 'anywhere',
            lineHeight: 1.5,
          }}
        >
          {this.props.output}
        </Typography>
      );
    }

    return (
      <Box>
        <Typography
          variant="body2"
          sx={{
            whiteSpace: 'pre-wrap',
            overflowWrap: 'anywhere',
            lineHeight: 1.5,
            display: '-webkit-box',
            WebkitBoxOrient: 'vertical',
            WebkitLineClamp: 3,
            overflow: 'hidden',
          }}
        >
          {lines.slice(0, 3).join('\n')}
        </Typography>
        <Box
          component="details"
          sx={{
            mt: 0.5,
            '& summary': {
              minHeight: 44,
              display: 'flex',
              alignItems: 'center',
              cursor: 'pointer',
              color: 'primary.main',
              fontSize: '0.875rem',
            },
          }}
        >
          <Box component="summary">Full report</Box>
          <Typography
            variant="body2"
            sx={{
              whiteSpace: 'pre-wrap',
              overflowWrap: 'anywhere',
              lineHeight: 1.5,
            }}
          >
            {this.props.output}
          </Typography>
        </Box>
      </Box>
    );
  }
}
