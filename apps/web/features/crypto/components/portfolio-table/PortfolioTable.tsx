import {
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
} from '@mui/material';
import { PureComponent } from 'react';
import PortfolioRow from '../portfolio-row/PortfolioRow';
import type { PortfolioTableProps } from './PortfolioTableProps';

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
    const headers = [
      'Coin',
      'Status',
      'Quantity',
      'Last buy',
      'Avg. cost (fees incl.)',
      'Current Price',
      'Value',
      'Engine unrealized P/L',
      'Engine realized P/L',
    ];

    return (
      <TableContainer
        tabIndex={0}
        role="region"
        aria-label="Crypto holdings and trading history"
        component={Paper}
        variant="outlined"
        sx={{ borderColor: '#f0f0f0', boxShadow: 'none', overflowX: 'auto' }}
      >
        <Table
          size="small"
          sx={{ minWidth: 1040 }}
        >
          <TableHead>
            <TableRow>
              {headers.map((head) => (
                <TableCell
                  key={head}
                  title={
                    head === 'Engine unrealized P/L'
                      ? 'Current value less verified cost; excludes future exit fees'
                      : undefined
                  }
                  align={
                    head === 'Coin' || head === 'Status' ? 'left' : 'right'
                  }
                  sx={{
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
                history={history[item.symbol]}
                historyLoading={Boolean(historyLoading[item.symbol])}
                historyError={historyErrors[item.symbol]}
                onRetry={() => {
                  onRetry(item.symbol);
                }}
              />
            ))}
            {!positions.length && (
              <TableRow>
                <TableCell
                  colSpan={9}
                  sx={{ py: 5, textAlign: 'center', color: '#707070' }}
                >
                  {hasRecords
                    ? 'No coins match this filter.'
                    : 'No verified holdings or trading history yet.'}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
    );
  }
}
