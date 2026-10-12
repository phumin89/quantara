"use client";

import { useState } from "react";
import { Box, Button, Stack, Tooltip, Typography } from "@mui/material";
import type { PortfolioResponse } from "../contracts/PortfolioResponse";
import formatTime from "../utils/formatTime";

export default function ConnectionStatus({
  data,
  error,
  refreshing,
}: {
  data: PortfolioResponse | null;
  error: string | null;
  refreshing: boolean;
}) {
  const [statusOpen, setStatusOpen] = useState(false);
  const status = error
    ? { label: "Error", color: "#dc2626", background: "#fef2f2" }
    : refreshing || data?.syncing
      ? { label: "Syncing", color: "#b45309", background: "#fffbeb" }
      : data?.updatedAt
        ? { label: "Connected", color: "#15803d", background: "#f0fdf4" }
        : { label: "Checking", color: "#525252", background: "#f3f3f3" };
  const details = (
    <Stack spacing={0.5} sx={{ fontSize: 12 }}>
      <Typography
        component="span"
        sx={{ color: "inherit", fontSize: 12, fontWeight: 600 }}
      >
        Last successful updates
      </Typography>
      <Typography component="span" sx={{ color: "inherit", fontSize: 12 }}>
        Wallet: {formatTime(data?.updatedAt || null)}
      </Typography>
      <Typography component="span" sx={{ color: "inherit", fontSize: 12 }}>
        Prices: {formatTime(data?.priceUpdatedAt || null)}
      </Typography>
      <Typography component="span" sx={{ color: "inherit", fontSize: 12 }}>
        History: {formatTime(data?.historyUpdatedAt || null)}
      </Typography>
      {data?.syncing && data.totalSymbols > 0 && (
        <Typography component="span" sx={{ color: "inherit", fontSize: 12 }}>
          Progress: {data.completedSymbols}/{data.totalSymbols} symbols
        </Typography>
      )}
    </Stack>
  );

  return (
    <Tooltip
      arrow
      open={statusOpen}
      disableHoverListener
      disableFocusListener
      disableTouchListener
      disableInteractive
      title={details}
      slotProps={{ tooltip: { sx: { maxWidth: 240 } } }}
    >
      <Button
        aria-label={`Bitkub status: ${status.label}`}
        onClick={() => {
          setStatusOpen(true);
        }}
        onMouseEnter={() => {
          setStatusOpen(true);
        }}
        onMouseLeave={() => {
          setStatusOpen(false);
        }}
        onFocus={() => {
          setStatusOpen(true);
        }}
        onBlur={() => {
          setStatusOpen(false);
        }}
        onKeyDown={(event) => {
          if (event.key === "Escape") {
            setStatusOpen(false);
          }
        }}
        sx={{
          minHeight: 44,
          minWidth: 44,
          p: 0.5,
          gap: 0.75,
          color: status.color,
          textTransform: "none",
          fontSize: 12,
          fontWeight: 600,
          borderRadius: "6px",
        }}
      >
        <Box
          aria-hidden="true"
          sx={{
            width: 8,
            height: 8,
            borderRadius: "50%",
            bgcolor: status.color,
          }}
        />
        <Box
          component="span"
          sx={{
            bgcolor: status.background,
            borderRadius: "4px",
            px: 0.75,
            py: 0.25,
          }}
        >
          {status.label}
        </Box>
      </Button>
    </Tooltip>
  );
}
