import { Fragment, PureComponent } from 'react';
import {
  Box,
  Button,
  Chip,
  Stack,
  TableCell,
  TableRow,
  Typography,
} from '@mui/material';
import KeyboardArrowDownOutlinedIcon from '@mui/icons-material/KeyboardArrowDownOutlined';
import KeyboardArrowUpOutlinedIcon from '@mui/icons-material/KeyboardArrowUpOutlined';
import AccountBalanceWalletOutlinedIcon from '@mui/icons-material/AccountBalanceWalletOutlined';
import CheckCircleOutlinedIcon from '@mui/icons-material/CheckCircleOutlined';
import FormatMoney from '../../utils/FormatMoney';
import FormatQuantity from '../../utils/FormatQuantity';
import ProfitLossAppearance from '../../presentation/ProfitLossAppearance';
import TradeHistory from '../trade-history/TradeHistory';
import type { PortfolioRowProps } from './PortfolioRowProps';
import { PositionStatus } from '../../contracts/PositionStatus';

export default class PortfolioRow extends PureComponent<PortfolioRowProps> {
  public override render(): React.ReactNode {
    const {
      item,
      expanded,
      onOpenHistory,
      history,
      historyLoading,
      historyError,
      onRetry,
    } = this.props;
    const isHolding = item.status === PositionStatus.Holding;
    const statusIcon = isHolding ? (
      <AccountBalanceWalletOutlinedIcon aria-hidden="true" />
    ) : (
      <CheckCircleOutlinedIcon aria-hidden="true" />
    );
    const lastBoughtAt = item.lastBoughtAt
      ? new Intl.DateTimeFormat('en-GB', {
          day: '2-digit',
          month: 'short',
          hour: '2-digit',
          minute: '2-digit',
          timeZone: 'Asia/Bangkok',
        }).format(new Date(item.lastBoughtAt))
      : null;
    const lastBuy =
      item.lastBuyPrice == null ? (
        '—'
      ) : (
        <span title="Latest confirmed engine buy; excludes fees">
          {FormatMoney.format(item.lastBuyPrice)}
          {lastBoughtAt ? (
            <Typography
              component="span"
              variant="caption"
              sx={{ display: 'block', color: 'text.secondary' }}
            >
              {lastBoughtAt}
            </Typography>
          ) : null}
        </span>
      );
    let averageCost: React.ReactNode = '—';
    if (item.averageCost !== null) {
      averageCost = (
        <span title="Verified engine cost per coin, including paid buy fees">
          {FormatMoney.format(item.averageCost)}
        </span>
      );
    } else if (isHolding) {
      averageCost = (
        <Chip
          label="Unknown cost"
          size="small"
          color="warning"
          sx={{ bgcolor: 'var(--surface-warning)', color: 'warning.main' }}
        />
      );
    }

    return (
      <Fragment>
        <TableRow
          hover
          sx={{ '& > *': { borderBottomColor: 'var(--border-panel)' } }}
        >
          <TableCell sx={{ py: 1, px: 2 }}>
            <Button
              size="small"
              aria-label={`View ${item.symbol} history`}
              onClick={onOpenHistory}
              aria-expanded={expanded}
              endIcon={
                expanded ? (
                  <KeyboardArrowUpOutlinedIcon />
                ) : (
                  <KeyboardArrowDownOutlinedIcon />
                )
              }
              sx={{
                p: 0,
                minWidth: 44,
                minHeight: 44,
                fontWeight: 600,
                textTransform: 'none',
                whiteSpace: 'nowrap',
              }}
            >
              {item.name}
              <Typography
                component="span"
                variant="caption"
                sx={{ ml: 0.75, color: 'text.secondary', fontWeight: 500 }}
              >
                · {item.symbol}
              </Typography>
            </Button>
            {isHolding && item.averageCost === null && (
              <Typography
                variant="caption"
                color="warning.main"
                sx={{ display: 'block' }}
              >
                Cost unavailable
              </Typography>
            )}
          </TableCell>
          <TableCell sx={{ py: 1, px: 2 }}>
            <Chip
              label={item.status}
              size="small"
              icon={statusIcon}
              color={isHolding ? 'success' : 'default'}
              sx={{
                bgcolor: isHolding
                  ? 'var(--surface-success)'
                  : 'var(--surface-warning)',
                color: isHolding ? 'success.main' : 'secondary.main',
                border: 0,
                '& .MuiChip-icon': { color: 'inherit' },
              }}
            />
          </TableCell>
          {[
            FormatQuantity.format(item.quantity),
            FormatMoney.format(item.currentPrice),
            FormatMoney.format(item.value),
          ].map((value, index) => (
            <TableCell
              key={index}
              align="right"
              sx={{
                py: 1,
                px: 2,
                whiteSpace: 'nowrap',
                fontVariantNumeric: 'tabular-nums',
              }}
            >
              {value}
            </TableCell>
          ))}
        </TableRow>
        <TableRow>
          <TableCell
            colSpan={5}
            sx={{ p: 0, border: 0 }}
          >
            {expanded && (
              <Box
                component="section"
                aria-label={`${item.symbol} engine cost and performance`}
                sx={{ p: 2.5, bgcolor: 'var(--surface-soft)' }}
              >
                <Typography
                  component="h3"
                  sx={{ fontSize: 14, fontWeight: 600, mb: 1.5 }}
                >
                  Engine cost and performance
                </Typography>
                <Stack
                  direction="row"
                  sx={{ gap: 3, flexWrap: 'wrap' }}
                >
                  <Box>
                    <Typography
                      variant="caption"
                      color="text.secondary"
                    >
                      Last confirmed buy
                    </Typography>
                    <Typography
                      component="div"
                      variant="body2"
                    >
                      {lastBuy}
                    </Typography>
                  </Box>
                  <Box>
                    <Typography
                      variant="caption"
                      color="text.secondary"
                    >
                      Avg. cost incl. fees
                    </Typography>
                    <Typography
                      component="div"
                      variant="body2"
                    >
                      {averageCost}
                    </Typography>
                  </Box>
                  <Box>
                    <Typography
                      variant="caption"
                      color="text.secondary"
                    >
                      Unrealized P/L
                    </Typography>
                    <Typography
                      variant="body2"
                      sx={{
                        color: ProfitLossAppearance.color(item.unrealizedPnl),
                      }}
                    >
                      {FormatMoney.pnl(item.unrealizedPnl)}
                    </Typography>
                  </Box>
                  <Box>
                    <Typography
                      variant="caption"
                      color="text.secondary"
                    >
                      Realized P/L
                    </Typography>
                    <Typography
                      variant="body2"
                      sx={{
                        color: ProfitLossAppearance.color(item.realizedPnl),
                      }}
                    >
                      {FormatMoney.pnl(item.realizedPnl)}
                    </Typography>
                  </Box>
                </Stack>
                <Typography
                  variant="caption"
                  color="text.secondary"
                  sx={{ display: 'block', mt: 1.5 }}
                >
                  Confirmed live engine fills only. Unknown cost remains
                  unknown; unrealized P/L excludes future exit fees.
                </Typography>
              </Box>
            )}
            <TradeHistory
              coinSymbol={item.symbol}
              expanded={expanded}
              loading={historyLoading}
              error={historyError}
              trades={history}
              onRetry={onRetry}
            />
          </TableCell>
        </TableRow>
      </Fragment>
    );
  }
}
