import {
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
} from "@mui/material";
import type { Position } from "../contracts/Position";
import type { Trade } from "../contracts/Trade";
import PortfolioRow from "./PortfolioRow";

export default function PortfolioTable({
  positions,
  hasRecords,
  expanded,
  onOpenHistory,
  history,
  historyLoading,
  historyErrors,
  onRetry,
}: {
  positions: Position[];
  hasRecords: boolean;
  expanded: string | null;
  onOpenHistory: (symbol: string) => void;
  history: Record<string, Trade[]>;
  historyLoading: Record<string, boolean>;
  historyErrors: Record<string, string>;
  onRetry: (symbol: string) => void;
}) {
  const headers = [
    "Coin",
    "Status",
    "Quantity",
    "Avg. Cost",
    "Current Price",
    "Value",
    "Unrealized P/L",
    "Realized P/L",
  ];

  return (
    <TableContainer
      tabIndex={0}
      role="region"
      aria-label="Crypto holdings and trading history"
      component={Paper}
      variant="outlined"
      sx={{ borderColor: "#f0f0f0", boxShadow: "none", overflowX: "auto" }}
    >
      <Table size="small" sx={{ minWidth: 1040 }}>
        <TableHead>
          <TableRow>
            {headers.map((head) => (
              <TableCell
                key={head}
                align={head === "Coin" || head === "Status" ? "left" : "right"}
                sx={{
                  fontSize: 12,
                  fontWeight: 600,
                  whiteSpace: "nowrap",
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
                colSpan={8}
                sx={{ py: 5, textAlign: "center", color: "#707070" }}
              >
                {hasRecords
                  ? "No coins match this filter."
                  : "No verified holdings or trading history yet."}
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
