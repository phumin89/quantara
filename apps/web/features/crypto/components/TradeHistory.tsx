import {
  Box,
  Button,
  Chip,
  Collapse,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import SouthWestOutlinedIcon from '@mui/icons-material/SouthWestOutlined';
import NorthEastOutlinedIcon from '@mui/icons-material/NorthEastOutlined';
import formatMoney from '../utils/formatMoney';
import formatQuantity from '../utils/formatQuantity';
import formatTime from '../utils/formatTime';
import { TradeSide } from '../contracts/TradeSide';
import type { TradeHistoryProps } from './TradeHistoryProps';

export default function TradeHistory({
  coinSymbol,
  expanded,
  loading,
  error,
  trades,
  onRetry,
}: TradeHistoryProps) {
  return (
    <Collapse
      in={expanded}
      unmountOnExit
    >
      <Box
        component="section"
        aria-label={`${coinSymbol} trade history`}
        sx={{ p: 2.5, bgcolor: '#f3f3f3' }}
      >
        {loading && (
          <Typography
            variant="body2"
            color="text.secondary"
          >
            Loading history…
          </Typography>
        )}
        {error && (
          <Stack
            direction="row"
            spacing={2}
            sx={{ alignItems: 'center' }}
          >
            <Typography variant="body2">{error}</Typography>
            <Button
              size="small"
              onClick={onRetry}
            >
              Retry
            </Button>
          </Stack>
        )}
        {!loading && !error && trades && trades.length > 0 && (
          <Table size="small">
            <TableHead>
              <TableRow>
                {['Date', 'Side', 'Quantity', 'Price', 'Fee', 'Fee credit'].map(
                  (head) => (
                    <TableCell
                      key={head}
                      align={
                        head === 'Date' || head === 'Side' ? 'left' : 'right'
                      }
                      sx={{
                        fontSize: 12,
                        fontWeight: 600,
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {head}
                    </TableCell>
                  ),
                )}
              </TableRow>
            </TableHead>
            <TableBody>
              {trades.map((trade) => (
                <TableRow key={trade.transactionId}>
                  <TableCell sx={{ whiteSpace: 'nowrap', fontSize: 13 }}>
                    {formatTime(trade.timestamp)}
                  </TableCell>
                  <TableCell sx={{ fontSize: 13 }}>
                    <Chip
                      label={trade.side}
                      size="small"
                      icon={
                        trade.side === TradeSide.Buy ? (
                          <SouthWestOutlinedIcon aria-hidden="true" />
                        ) : (
                          <NorthEastOutlinedIcon aria-hidden="true" />
                        )
                      }
                      color={trade.side === TradeSide.Buy ? 'info' : undefined}
                      sx={
                        trade.side === TradeSide.Sell
                          ? {
                              bgcolor: '#ffedd5',
                              color: '#9a3412',
                              '& .MuiChip-icon': { color: 'inherit' },
                            }
                          : {
                              bgcolor: 'info.light',
                              color: 'info.main',
                              '& .MuiChip-icon': { color: 'inherit' },
                            }
                      }
                    />
                  </TableCell>
                  <TableCell
                    align="right"
                    sx={{ fontSize: 13, fontVariantNumeric: 'tabular-nums' }}
                  >
                    {formatQuantity(trade.quantity)}
                  </TableCell>
                  <TableCell
                    align="right"
                    sx={{ fontSize: 13, fontVariantNumeric: 'tabular-nums' }}
                  >
                    {formatMoney(trade.price)}
                  </TableCell>
                  <TableCell
                    align="right"
                    sx={{ fontSize: 13, fontVariantNumeric: 'tabular-nums' }}
                  >
                    {formatMoney(trade.fee)}
                  </TableCell>
                  <TableCell
                    align="right"
                    sx={{ fontSize: 13, fontVariantNumeric: 'tabular-nums' }}
                  >
                    {formatMoney(trade.feeCredit)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        {!loading && !error && trades && trades.length === 0 && (
          <Typography
            variant="body2"
            color="text.secondary"
          >
            No verified trades found.
          </Typography>
        )}
      </Box>
    </Collapse>
  );
}
