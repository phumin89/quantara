import {
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { PureComponent } from 'react';
import PortfolioRow from '../portfolio-row/PortfolioRow';
import type { PortfolioTableProps } from './PortfolioTableProps';
import { PortfolioFilter } from '../../contracts/PortfolioFilter';

export default class PortfolioTable extends PureComponent<PortfolioTableProps> {
  public override render(): React.ReactNode {
    const {
      positions,
      hasRecords,
      expanded,
      onOpenHistory,
      history,
      historyLoading,
      historyErrors,
      onRetry,
    } = this.props;
    const headers = ['Coin', 'Status', 'Quantity', 'Current Price', 'Value'];
    let emptyMessage = 'No verified holdings or trading history yet.';
    if (!this.props.holdingsKnown) {
      emptyMessage =
        'Wallet holdings unavailable. A verified snapshot is required.';
    } else if (this.props.filter === PortfolioFilter.Holding) {
      emptyMessage = 'No assets currently held in this snapshot.';
    } else if (hasRecords) {
      emptyMessage = 'No coins match this filter.';
    }

    if (positions.length === 0) {
      return (
        <Paper
          variant="outlined"
          role="region"
          aria-label="Crypto holdings and trading history"
          sx={{
            p: 'var(--panel-padding)',
            borderColor: 'var(--border-panel)',
            bgcolor: 'var(--surface-raised)',
            borderRadius: 'var(--radius-panel)',
          }}
        >
          <Typography
            variant="body2"
            color="text.secondary"
          >
            {emptyMessage}
          </Typography>
        </Paper>
      );
    }

    return (
      <>
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: { xs: 'block', sm: 'none' } }}
        >
          Scroll to view quantities and values.
        </Typography>
        <TableContainer
          tabIndex={0}
          role="region"
          aria-label="Crypto holdings and trading history"
          component={Paper}
          variant="outlined"
          sx={{
            borderColor: 'var(--border-panel)',
            bgcolor: 'var(--surface-raised)',
            borderRadius: 'var(--radius-panel)',
            boxShadow: 'var(--shadow-panel)',
            overflowX: 'auto',
          }}
        >
          <Table
            size="small"
            stickyHeader
            sx={{ minWidth: 600 }}
          >
            <TableHead>
              <TableRow>
                {headers.map((head) => (
                  <TableCell
                    key={head}
                    align={
                      head === 'Coin' || head === 'Status' ? 'left' : 'right'
                    }
                    sx={{
                      bgcolor: 'primary.main',
                      color: 'primary.contrastText',
                      fontSize: 12,
                      fontWeight: 600,
                      whiteSpace: 'nowrap',
                      height: 40,
                      py: 1,
                      px: 2,
                    }}
                  >
                    {head}
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {positions.map((item) => (
                <PortfolioRow
                  key={item.symbol}
                  item={item}
                  expanded={expanded === item.symbol}
                  onOpenHistory={() => {
                    onOpenHistory(item.symbol);
                  }}
                  history={
                    Object.hasOwn(history, item.symbol)
                      ? history[item.symbol]
                      : undefined
                  }
                  historyLoading={
                    Object.hasOwn(historyLoading, item.symbol) &&
                    Boolean(historyLoading[item.symbol])
                  }
                  historyError={
                    Object.hasOwn(historyErrors, item.symbol)
                      ? historyErrors[item.symbol]
                      : undefined
                  }
                  onRetry={() => {
                    onRetry(item.symbol);
                  }}
                />
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </>
    );
  }
}
