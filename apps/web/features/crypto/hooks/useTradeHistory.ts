"use client";

import { useEffect, useRef, useState } from "react";
import { getTradeHistory } from "../api/getTradeHistory";
import type { Trade } from "../contracts/Trade";

export function useTradeHistory(historyUpdatedAt: string | null | undefined) {
  const [expanded, setExpanded] = useState<string | null>(null);
  const [history, setHistory] = useState<Record<string, Trade[]>>({});
  const [historyLoading, setHistoryLoading] = useState<Record<string, boolean>>(
    {},
  );
  const [historyErrors, setHistoryErrors] = useState<Record<string, string>>(
    {},
  );
  const requests = useRef(new Set<AbortController>());
  useEffect(
    () => () => {
      for (const request of requests.current) {
        request.abort();
      }
    },
    [],
  );
  useEffect(() => {
    setHistory({});
    setHistoryErrors({});
    setExpanded(null);
  }, [historyUpdatedAt]);
  const openHistory = async (symbol: string, force = false) => {
    setExpanded(force ? symbol : expanded === symbol ? null : symbol);
    if (!force && history[symbol]) {
      return;
    }

    setHistoryLoading((current) => ({ ...current, [symbol]: true }));
    setHistoryErrors((current) =>
      Object.fromEntries(
        Object.entries(current).filter(([key]) => key !== symbol),
      ),
    );
    const controller = new AbortController();
    requests.current.add(controller);
    try {
      const response = await getTradeHistory(symbol, controller.signal);
      setHistory((current) => ({ ...current, [symbol]: response.trades }));
    } catch (cause) {
      if (controller.signal.aborted) {
        return;
      }

      setHistoryErrors((current) => ({
        ...current,
        [symbol]:
          cause instanceof Error ? cause.message : "Unable to load history.",
      }));
    } finally {
      requests.current.delete(controller);
      setHistoryLoading((current) => ({ ...current, [symbol]: false }));
    }
  };

  return { expanded, history, historyLoading, historyErrors, openHistory };
}
