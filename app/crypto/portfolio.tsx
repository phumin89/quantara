"use client";

import {
  Fragment,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  Divider,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from "@mui/material";
import RefreshOutlinedIcon from "@mui/icons-material/RefreshOutlined";
import KeyboardArrowDownOutlinedIcon from "@mui/icons-material/KeyboardArrowDownOutlined";
import KeyboardArrowUpOutlinedIcon from "@mui/icons-material/KeyboardArrowUpOutlined";
import GridViewOutlinedIcon from "@mui/icons-material/GridViewOutlined";
import AccountBalanceWalletOutlinedIcon from "@mui/icons-material/AccountBalanceWalletOutlined";
import CheckCircleOutlinedIcon from "@mui/icons-material/CheckCircleOutlined";
import SouthWestOutlinedIcon from "@mui/icons-material/SouthWestOutlined";
import NorthEastOutlinedIcon from "@mui/icons-material/NorthEastOutlined";

type Position = {
  symbol: string;
  name: string;
  status: "Holding" | "Closed";
  quantity: number;
  averageCost: number | null;
  currentPrice: number | null;
  value: number | null;
  unrealizedPnl: number | null;
  realizedPnl: number | null;
  costStatus: "Unknown";
};
type Trade = {
  transactionId: string;
  symbol: string;
  side: "buy" | "sell";
  quantity: number;
  price: number;
  fee: number;
  feeCredit: number;
  timestamp: string;
};
type PortfolioResponse = {
  positions: Position[];
  summary: {
    totalAssets: number | null;
    availableThb: number | null;
    holdingCount: number | null;
  };
  updatedAt: string | null;
  priceUpdatedAt: string | null;
  historyUpdatedAt: string | null;
  historyCoverage: string;
  syncing: boolean;
  completedSymbols: number;
  totalSymbols: number;
  error: string | null;
};

const money = (value: number | null) =>
  value == null
    ? "—"
    : `฿${value.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
const quantity = (value: number) =>
  value.toLocaleString("en-US", { maximumFractionDigits: 12 });
const time = (value: string | null) =>
  value
    ? new Date(value).toLocaleString("en-US", {
        dateStyle: "medium",
        timeStyle: "short",
      })
    : "—";

function SummaryMetric({
  testId,
  label,
  value,
  caption,
}: {
  testId: string;
  label: string;
  value: string;
  caption: string;
}) {
  return (
    <Box sx={{ flex: 1, minWidth: 0, px: { xs: 0, sm: 2 }, py: { xs: 1, sm: 0 } }}>
      <Typography sx={{ fontSize: 12, color: "#707070", fontWeight: 600 }}>{label}</Typography>
      <Typography data-testid={testId} sx={{ mt: 0.5, fontSize: 24, lineHeight: 1.2, fontWeight: 650, fontVariantNumeric: "tabular-nums" }}>{value}</Typography>
      <Typography sx={{ mt: 0.5, fontSize: 12, color: "#707070" }}>{caption}</Typography>
    </Box>
  );
}

export default function Portfolio() {
  const [data, setData] = useState<PortfolioResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<"All" | "Holding" | "Closed">("All");
  const [expanded, setExpanded] = useState<string | null>(null);
  const [history, setHistory] = useState<Record<string, Trade[]>>({});
  const [historyErrors, setHistoryErrors] = useState<Record<string, string>>(
    {},
  );
  const [historyLoading, setHistoryLoading] = useState<Record<string, boolean>>(
    {},
  );
  const [refreshing, setRefreshing] = useState(false);
  const pollingRef = useRef(false);
  const historyRequests = useRef(new Set<AbortController>());
  useEffect(
    () => () => {
      for (const controller of historyRequests.current) controller.abort();
    },
    [],
  );
  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const response = await fetch("/api/crypto/portfolio", {
        signal,
        cache: "no-store",
      });
      const body = await response.json();
      if (!response.ok)
        throw new Error(body.error || "Unable to load portfolio.");
      setData(body);
      setError(body.error || null);
    } catch (cause) {
      if (!(cause instanceof Error && cause.name === "AbortError"))
        setError(
          cause instanceof Error ? cause.message : "Unable to load portfolio.",
        );
    }
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);
  useEffect(() => {
    if (!data?.syncing) return;
    const controller = new AbortController();
    const timer = window.setInterval(() => {
      if (pollingRef.current) return;
      pollingRef.current = true;
      void load(controller.signal).finally(() => {
        pollingRef.current = false;
      });
    }, 2000);
    return () => {
      window.clearInterval(timer);
      controller.abort();
    };
  }, [data?.syncing, load]);
  useEffect(() => {
    setHistory({});
    setHistoryErrors({});
    setExpanded(null);
  }, [data?.historyUpdatedAt]);
  const openHistory = async (symbol: string, force = false) => {
    setExpanded(force ? symbol : expanded === symbol ? null : symbol);
    if (!force && history[symbol]) return;
    setHistoryLoading((current) => ({ ...current, [symbol]: true }));
    setHistoryErrors((current) => {
      const next = { ...current };
      delete next[symbol];
      return next;
    });
    const controller = new AbortController();
    historyRequests.current.add(controller);
    try {
      const response = await fetch(
        `/api/crypto/history?coin=${encodeURIComponent(symbol)}`,
        { cache: "no-store", signal: controller.signal },
      );
      const body = await response.json();
      if (!response.ok)
        throw new Error(body.error || "Unable to load history.");
      setHistory((current) => ({ ...current, [symbol]: body.trades || [] }));
    } catch (cause) {
      if (controller.signal.aborted) return;
      setHistoryErrors((current) => ({
        ...current,
        [symbol]:
          cause instanceof Error ? cause.message : "Unable to load history.",
      }));
    } finally {
      historyRequests.current.delete(controller);
      setHistoryLoading((current) => ({ ...current, [symbol]: false }));
    }
  };
  const refresh = async () => {
    setRefreshing(true);
    try {
      const response = await fetch("/api/crypto/sync", { method: "POST" });
      const body = await response.json().catch(() => ({}));
      if (!response.ok)
        throw new Error(body.error || "Unable to sync Bitkub data.");
      await load();
    } catch (cause) {
      setError(
        cause instanceof Error ? cause.message : "Unable to sync Bitkub data.",
      );
    } finally {
      setRefreshing(false);
    }
  };
  const positions = useMemo(
    () =>
      (data?.positions || [])
        .filter((item) => filter === "All" || item.status === filter)
        .sort(
          (a, b) =>
            Number(b.status === "Holding") - Number(a.status === "Holding") ||
            a.symbol.localeCompare(b.symbol),
        ),
    [data?.positions, filter],
  );
  const stale = Boolean(error && data);
  return (
    <Stack spacing={4} sx={{ width: "100%", maxWidth: 1280 }}>
      <Stack
        direction="row"
        spacing={2}
        sx={{ justifyContent: "space-between", alignItems: "center" }}
      >
        <Box sx={{ minWidth: 0 }}>
          <Typography
            component="h1"
            sx={{ fontSize: 32, fontWeight: 650, lineHeight: 1.13, m: 0 }}
          >
            Crypto
          </Typography>
          <Typography sx={{ mt: 1, fontSize: 14, color: "#707070" }}>
            Bitkub portfolio
            {data
              ? ` · ${data.positions.length} ${data.positions.length === 1 ? "asset" : "assets"} tracked`
              : ""}
          </Typography>
        </Box>
        <Button
          variant="outlined"
          startIcon={<RefreshOutlinedIcon />}
          onClick={() => void refresh()}
          disabled={refreshing || data?.syncing}
          sx={{
            borderColor: "#e0e0e0",
            color: "#141414",
            minHeight: 44,
            px: 2.5,
            textTransform: "none",
            flexShrink: 0,
          }}
        >
          {refreshing ? "Syncing…" : "Refresh"}
        </Button>
      </Stack>
      {data?.syncing && (
        <Typography variant="body2" color="text.secondary">
          Syncing Bitkub history
          {data.totalSymbols
            ? ` · ${data.completedSymbols}/${data.totalSymbols} symbols`
            : ""}
          …
        </Typography>
      )}
      {stale && (
        <Paper
          variant="outlined"
          sx={{ p: 2, borderColor: "#fecaca", bgcolor: "#fef2f2" }}
        >
          <Typography variant="body2">
            Showing the latest saved snapshot. {error}
          </Typography>
        </Paper>
      )}
      {!data && !error && (
        <Box sx={{ display: "flex", justifyContent: "center", py: 8 }}>
          <CircularProgress size={24} />
        </Box>
      )}
      {!data && error && (
        <Stack spacing={2} sx={{ alignItems: "flex-start" }}>
          <Typography sx={{ color: "error.main" }}>{error}</Typography>
          <Button
            variant="outlined"
            onClick={() => void load()}
            sx={{ borderRadius: "6px" }}
          >
            Retry
          </Button>
        </Stack>
      )}
      {data && (
        <>
          <Paper
            component="section"
            aria-label="Portfolio summary"
            variant="outlined"
            sx={{
              borderColor: "#e0e0e0",
              borderRadius: "8px",
              boxShadow: "none",
              p: { xs: 1.5, sm: 2 },
            }}
          >
            <Stack direction={{ xs: "column", sm: "row" }} divider={<Divider flexItem sx={{ borderBottomWidth: { xs: 1, sm: 0 }, borderRightWidth: { xs: 0, sm: 1 } }} />} spacing={0}>
              <SummaryMetric
                testId="total-assets"
                label="Total assets"
                value={data.summary.totalAssets == null ? "—" : money(data.summary.totalAssets)}
                caption={data.summary.totalAssets == null ? (data.updatedAt ? "Price data incomplete" : "Awaiting wallet data") : "Held crypto value"}
              />
              <SummaryMetric
                testId="available-thb"
                label="Available THB"
                value={data.summary.availableThb == null ? "—" : money(data.summary.availableThb)}
                caption={data.summary.availableThb == null ? (data.updatedAt ? "Balance unavailable" : "Awaiting wallet data") : "Available to trade"}
              />
              <SummaryMetric
                testId="holding-count"
                label="Holding"
                value={data.summary.holdingCount == null ? "—" : String(data.summary.holdingCount)}
                caption="Positive crypto balances"
              />
            </Stack>
          </Paper>
          <Stack
            direction={{ xs: "column", sm: "row" }}
            spacing={2}
            sx={{
              justifyContent: "space-between",
              alignItems: { sm: "center" },
            }}
          >
            <ToggleButtonGroup
              exclusive
              value={filter}
              onChange={(_, value) => value && setFilter(value)}
              aria-label="Portfolio filter"
              size="small"
              sx={{
                bgcolor: "#f3f3f3",
                borderRadius: "6px",
                p: 0.5,
                width: "fit-content",
                "& .MuiToggleButtonGroup-grouped": {
                  border: 0,
                  borderRadius: "6px",
                  minHeight: 44,
                  px: 1.25,
                  gap: 0.75,
                  textTransform: "none",
                  fontSize: 14,
                },
              }}
            >
              {(["All", "Holding", "Closed"] as const).map((value) => (
                <ToggleButton
                  key={value}
                  value={value}
                  sx={{
                    border: 0,
                    ...(value === "All"
                      ? {
                          color: "#525252",
                          "&.Mui-selected": {
                            bgcolor: "#fff",
                            color: "#141414",
                            boxShadow: "inset 0 0 0 1px #bdbdbd",
                          },
                        }
                      : value === "Holding"
                        ? {
                            color: "#166534",
                            "&.Mui-selected": {
                              bgcolor: "#dcfce7",
                              color: "#166534",
                              boxShadow: "inset 0 0 0 1px #166534",
                            },
                          }
                        : {
                            color: "#6b21a8",
                            "&.Mui-selected": {
                              bgcolor: "#f3e8ff",
                              color: "#6b21a8",
                              boxShadow: "inset 0 0 0 1px #6b21a8",
                            },
                          }),
                  }}
                >
                  {value === "All" ? (
                    <GridViewOutlinedIcon aria-hidden="true" sx={{ fontSize: 18 }} />
                  ) : value === "Holding" ? (
                    <AccountBalanceWalletOutlinedIcon aria-hidden="true" sx={{ fontSize: 18 }} />
                  ) : (
                    <CheckCircleOutlinedIcon aria-hidden="true" sx={{ fontSize: 18 }} />
                  )}
                  {value}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
            <Typography
              variant="caption"
              sx={{
                alignSelf: { xs: "flex-start", sm: "center" },
                color: "#707070",
              }}
            >
              Prices updated {time(data.priceUpdatedAt)}
            </Typography>
          </Stack>
          <Box sx={{ color: "#707070", fontSize: 14 }}>
            <Typography
              variant="body2"
              sx={{
                fontSize: 14,
                color: "warning.main",
                bgcolor: "warning.light",
                px: 1.5,
                py: 1,
                borderRadius: "6px",
              }}
            >
              90-day exchange history · Cost &amp; P/L unverified · Broker
              conversions excluded
            </Typography>
          </Box>
          <TableContainer
            tabIndex={0}
            role="region"
            aria-label="Crypto holdings and trading history"
            component={Paper}
            variant="outlined"
            sx={{
              borderColor: "#f0f0f0",
              boxShadow: "none",
              overflowX: "auto",
            }}
          >
            <Table size="small" sx={{ minWidth: 1040 }}>
              <TableHead>
                <TableRow>
                  {[
                    "Coin",
                    "Status",
                    "Quantity",
                    "Avg. Cost",
                    "Current Price",
                    "Value",
                    "Unrealized P/L",
                    "Realized P/L",
                  ].map((head) => (
                    <TableCell
                      key={head}
                      align={
                        head === "Coin" || head === "Status" ? "left" : "right"
                      }
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
                  <Fragment key={item.symbol}>
                    <TableRow
                      hover
                      sx={{ "& > *": { borderBottomColor: "#e0e0e0" } }}
                    >
                      <TableCell sx={{ py: 1, px: 2 }}>
                        <Button
                          size="small"
                          aria-label={`View ${item.symbol} history`}
                          onClick={() => void openHistory(item.symbol)}
                          aria-expanded={expanded === item.symbol}
                          endIcon={
                            expanded === item.symbol ? (
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
                          color={
                            item.status === "Holding" ? "success" : "default"
                          }
                          sx={{
                            bgcolor:
                              item.status === "Holding"
                                ? "success.light"
                                : "#f3e8ff",
                            color:
                              item.status === "Holding"
                                ? "success.main"
                                : "#6b21a8",
                            border: 0,
                            "& .MuiChip-icon": { color: "inherit" },
                          }}
                        />
                      </TableCell>
                      <TableCell
                        align="right"
                        sx={{
                          py: 1,
                          px: 2,
                          whiteSpace: "nowrap",
                          fontVariantNumeric: "tabular-nums",
                        }}
                      >
                        {quantity(item.quantity)}
                      </TableCell>
                      <TableCell
                        align="right"
                        sx={{
                          py: 1,
                          px: 2,
                          whiteSpace: "nowrap",
                          fontVariantNumeric: "tabular-nums",
                        }}
                      >
                        {item.status === "Closed" ? (
                          "—"
                        ) : item.costStatus === "Unknown" ? (
                          <Chip
                            label="Unknown cost"
                            size="small"
                            color="warning"
                            sx={{ bgcolor: "warning.light", color: "warning.main" }}
                          />
                        ) : (
                          money(item.averageCost)
                        )}
                      </TableCell>
                      <TableCell
                        align="right"
                        sx={{
                          py: 1,
                          px: 2,
                          whiteSpace: "nowrap",
                          fontVariantNumeric: "tabular-nums",
                        }}
                      >
                        {money(item.currentPrice)}
                      </TableCell>
                      <TableCell
                        align="right"
                        sx={{
                          py: 1,
                          px: 2,
                          whiteSpace: "nowrap",
                          fontVariantNumeric: "tabular-nums",
                        }}
                      >
                        {money(item.value)}
                      </TableCell>
                      <TableCell
                        align="right"
                        sx={{
                          py: 1,
                          px: 2,
                          whiteSpace: "nowrap",
                          fontVariantNumeric: "tabular-nums",
                        }}
                      >
                        {item.status === "Closed" ||
                        item.costStatus === "Unknown"
                          ? "—"
                          : <Box component="span" sx={{ color: item.unrealizedPnl == null || item.unrealizedPnl === 0 ? "text.primary" : item.unrealizedPnl > 0 ? "success.main" : "error.main" }}>{money(item.unrealizedPnl)}</Box>}
                      </TableCell>
                      <TableCell
                        align="right"
                        sx={{
                          py: 1,
                          px: 2,
                          whiteSpace: "nowrap",
                          fontVariantNumeric: "tabular-nums",
                        }}
                      >
                        {item.costStatus === "Unknown"
                          ? "—"
                          : <Box component="span" sx={{ color: item.realizedPnl == null || item.realizedPnl === 0 ? "text.primary" : item.realizedPnl > 0 ? "success.main" : "error.main" }}>{money(item.realizedPnl)}</Box>}
                      </TableCell>
                    </TableRow>
                    <TableRow>
                      <TableCell colSpan={8} sx={{ p: 0, border: 0 }}>
                        <Collapse in={expanded === item.symbol} unmountOnExit>
                          <Box sx={{ p: 2.5, bgcolor: "#f3f3f3" }}>
                            {historyLoading[item.symbol] && (
                              <Typography
                                variant="body2"
                                color="text.secondary"
                              >
                                Loading history…
                              </Typography>
                            )}
                            {historyErrors[item.symbol] && (
                              <Stack
                                direction="row"
                                spacing={2}
                                sx={{ alignItems: "center" }}
                              >
                                <Typography variant="body2">
                                  {historyErrors[item.symbol]}
                                </Typography>
                                <Button
                                  size="small"
                                  onClick={() =>
                                    void openHistory(item.symbol, true)
                                  }
                                >
                                  Retry
                                </Button>
                              </Stack>
                            )}
                            {!historyLoading[item.symbol] &&
                              history[item.symbol] &&
                              (history[item.symbol].length ? (
                                <Table size="small">
                                  <TableHead>
                                    <TableRow>
                                      {[
                                        "Date",
                                        "Side",
                                        "Quantity",
                                        "Price",
                                        "Fee",
                                        "Fee credit",
                                      ].map((head) => (
                                        <TableCell
                                          key={head}
                                          align={
                                            head === "Date" || head === "Side"
                                              ? "left"
                                              : "right"
                                          }
                                          sx={{
                                            fontSize: 12,
                                            fontWeight: 600,
                                            whiteSpace: "nowrap",
                                          }}
                                        >
                                          {head}
                                        </TableCell>
                                      ))}
                                    </TableRow>
                                  </TableHead>
                                  <TableBody>
                                    {history[item.symbol].map((trade) => (
                                      <TableRow key={trade.transactionId}>
                                        <TableCell
                                          sx={{
                                            whiteSpace: "nowrap",
                                            fontSize: 13,
                                          }}
                                        >
                                          {time(trade.timestamp)}
                                        </TableCell>
                                        <TableCell sx={{ fontSize: 13 }}>
                                          <Chip
                                            label={trade.side}
                                            size="small"
                                            icon={
                                              trade.side === "buy" ? (
                                                <SouthWestOutlinedIcon aria-hidden="true" />
                                              ) : (
                                                <NorthEastOutlinedIcon aria-hidden="true" />
                                              )
                                            }
                                            color={
                                              trade.side === "buy"
                                                ? "info"
                                                : undefined
                                            }
                                            sx={
                                              trade.side === "sell"
                                                ? {
                                                    bgcolor: "#ffedd5",
                                                    color: "#9a3412",
                                                    "& .MuiChip-icon": { color: "inherit" },
                                                  }
                                                : {
                                                    bgcolor: "info.light",
                                                    color: "info.main",
                                                    "& .MuiChip-icon": { color: "inherit" },
                                                  }
                                            }
                                          />
                                        </TableCell>
                                        <TableCell
                                          align="right"
                                          sx={{
                                            whiteSpace: "nowrap",
                                            fontSize: 13,
                                            fontVariantNumeric: "tabular-nums",
                                          }}
                                        >
                                          {quantity(trade.quantity)}
                                        </TableCell>
                                        <TableCell
                                          align="right"
                                          sx={{
                                            whiteSpace: "nowrap",
                                            fontSize: 13,
                                            fontVariantNumeric: "tabular-nums",
                                          }}
                                        >
                                          {money(trade.price)}
                                        </TableCell>
                                        <TableCell
                                          align="right"
                                          sx={{
                                            whiteSpace: "nowrap",
                                            fontSize: 13,
                                            fontVariantNumeric: "tabular-nums",
                                          }}
                                        >
                                          {money(trade.fee)}
                                        </TableCell>
                                        <TableCell
                                          align="right"
                                          sx={{
                                            whiteSpace: "nowrap",
                                            fontSize: 13,
                                            fontVariantNumeric: "tabular-nums",
                                          }}
                                        >
                                          {money(trade.feeCredit)}
                                        </TableCell>
                                      </TableRow>
                                    ))}
                                  </TableBody>
                                </Table>
                              ) : (
                                <Typography
                                  variant="body2"
                                  color="text.secondary"
                                >
                                  No verified trades found.
                                </Typography>
                              ))}
                          </Box>
                        </Collapse>
                      </TableCell>
                    </TableRow>
                  </Fragment>
                ))}
                {!positions.length && (
                  <TableRow>
                    <TableCell colSpan={8}>
                      <Typography
                        sx={{ py: 5, textAlign: "center" }}
                        color="text.secondary"
                      >
                        {data.positions.length
                          ? "No coins match this filter."
                          : "No verified holdings or trading history yet."}
                      </Typography>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </TableContainer>
          <Box
            component="details"
            sx={{ color: "#707070", fontSize: 14, mt: -1 }}
          >
            <Box
              component="summary"
              sx={{ cursor: "pointer", color: "#141414", fontWeight: 600 }}
            >
              Data coverage
            </Box>
            <Typography variant="body2" sx={{ mt: 1, fontSize: 14 }}>
              {data.historyCoverage ||
                "Bitkub exposes recent history (90 days). Earlier activity may be missing; profit and cost stay unknown until verified."}
            </Typography>
          </Box>
        </>
      )}
    </Stack>
  );
}
