"use client";

import { useMemo, useState } from "react";
import {
  Box,
  Button,
  CircularProgress,
  Paper,
  Stack,
  Typography,
} from "@mui/material";
import RefreshOutlinedIcon from "@mui/icons-material/RefreshOutlined";
import { usePortfolio } from "../hooks/usePortfolio";
import { useTradeHistory } from "../hooks/useTradeHistory";
import ConnectionStatus from "./ConnectionStatus";
import PortfolioSummary from "./PortfolioSummary";
import PortfolioFilters from "./PortfolioFilters";
import PortfolioTable from "./PortfolioTable";

export default function CryptoPage() {
  const { data, error, refreshing, load, refresh } = usePortfolio();
  const { expanded, history, historyLoading, historyErrors, openHistory } =
    useTradeHistory(data?.historyUpdatedAt);
  const [filter, setFilter] = useState<"All" | "Holding" | "Closed">("All");
  const positions = useMemo(
    () =>
      (data?.positions ?? [])
        .filter((item) => filter === "All" || item.status === filter)
        .sort(
          (a, b) =>
            Number(b.status === "Holding") - Number(a.status === "Holding") ||
            a.symbol.localeCompare(b.symbol),
        ),
    [data?.positions, filter],
  );

  return (
    <Stack spacing={4} sx={{ width: "100%", maxWidth: 1280 }}>
      <Stack
        direction="row"
        spacing={2}
        sx={{ justifyContent: "space-between", alignItems: "center" }}
      >
        <Box sx={{ minWidth: 0 }}>
          <Stack
            direction="row"
            spacing={1}
            sx={{ alignItems: "center", flexWrap: "wrap" }}
          >
            <Typography
              component="h1"
              sx={{ fontSize: 32, fontWeight: 650, lineHeight: 1.13, m: 0 }}
            >
              Crypto
            </Typography>
            <ConnectionStatus
              data={data}
              error={error}
              refreshing={refreshing}
            />
          </Stack>
          <Typography sx={{ mt: 1, fontSize: 14, color: "#707070" }}>
            Bitkub portfolio
            {data
              ? ` · ${String(data.positions.length)} ${data.positions.length === 1 ? "asset" : "assets"} tracked`
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
      {error && data && (
        <Paper
          variant="outlined"
          sx={{ p: 2, borderColor: "#fecaca", bgcolor: "#fef2f2" }}
        >
          <Typography variant="body2">
            Showing the latest saved snapshot. {error}
          </Typography>
          <Button
            variant="outlined"
            onClick={() => void refresh()}
            disabled={refreshing || data.syncing}
            sx={{ mt: 1, minHeight: 44, borderRadius: "6px" }}
          >
            Retry
          </Button>
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
          <PortfolioSummary data={data} />
          <PortfolioFilters filter={filter} onChange={setFilter} />
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
          <PortfolioTable
            positions={positions}
            hasRecords={data.positions.length > 0}
            expanded={expanded}
            onOpenHistory={(symbol) => void openHistory(symbol)}
            history={history}
            historyLoading={historyLoading}
            historyErrors={historyErrors}
            onRetry={(symbol) => void openHistory(symbol, true)}
          />
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
            <Typography variant="body2" sx={{ mt: 1.5 }}>
              {data.historyCoverage}
            </Typography>
          </Box>
        </>
      )}
    </Stack>
  );
}
