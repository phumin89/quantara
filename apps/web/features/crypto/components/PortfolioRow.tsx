import { Fragment } from "react";
import { Button, Chip, TableCell, TableRow, Typography } from "@mui/material";
import KeyboardArrowDownOutlinedIcon from "@mui/icons-material/KeyboardArrowDownOutlined";
import KeyboardArrowUpOutlinedIcon from "@mui/icons-material/KeyboardArrowUpOutlined";
import AccountBalanceWalletOutlinedIcon from "@mui/icons-material/AccountBalanceWalletOutlined";
import CheckCircleOutlinedIcon from "@mui/icons-material/CheckCircleOutlined";
import type { Position } from "../contracts/Position";
import type { Trade } from "../contracts/Trade";
import formatMoney from "../utils/formatMoney";
import formatQuantity from "../utils/formatQuantity";
import TradeHistory from "./TradeHistory";

export default function PortfolioRow({
  item,
  expanded,
  onOpenHistory,
  history,
  historyLoading,
  historyError,
  onRetry,
}: {
  item: Position;
  expanded: boolean;
  onOpenHistory: () => void;
  history: Trade[] | undefined;
  historyLoading: boolean;
  historyError: string | undefined;
  onRetry: () => void;
}) {
  return (
    <Fragment>
      <TableRow hover sx={{ "& > *": { borderBottomColor: "#e0e0e0" } }}>
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
              textTransform: "none",
              whiteSpace: "nowrap",
            }}
          >
            {item.name}
            <Typography
              component="span"
              variant="caption"
              sx={{ ml: 0.75, color: "#707070", fontWeight: 500 }}
            >
              · {item.symbol}
            </Typography>
          </Button>
        </TableCell>
        <TableCell sx={{ py: 1, px: 2 }}>
          <Chip
            label={item.status}
            size="small"
            icon={
              item.status === "Holding" ? (
                <AccountBalanceWalletOutlinedIcon aria-hidden="true" />
              ) : (
                <CheckCircleOutlinedIcon aria-hidden="true" />
              )
            }
            color={item.status === "Holding" ? "success" : "default"}
            sx={{
              bgcolor: item.status === "Holding" ? "success.light" : "#f3e8ff",
              color: item.status === "Holding" ? "success.main" : "#6b21a8",
              border: 0,
              "& .MuiChip-icon": { color: "inherit" },
            }}
          />
        </TableCell>
        {[
          formatQuantity(item.quantity),
          item.status === "Closed" ? (
            "—"
          ) : (
            <Chip
              key="cost"
              label="Unknown cost"
              size="small"
              color="warning"
              sx={{ bgcolor: "warning.light", color: "warning.main" }}
            />
          ),
          formatMoney(item.currentPrice),
          formatMoney(item.value),
          "—",
          "—",
        ].map((value, index) => (
          <TableCell
            key={index}
            align="right"
            sx={{
              py: 1,
              px: 2,
              whiteSpace: "nowrap",
              fontVariantNumeric: "tabular-nums",
            }}
          >
            {value}
          </TableCell>
        ))}
      </TableRow>
      <TableRow>
        <TableCell colSpan={8} sx={{ p: 0, border: 0 }}>
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
