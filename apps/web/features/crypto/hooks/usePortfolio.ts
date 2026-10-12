"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { getPortfolio } from "../api/getPortfolio";
import { syncPortfolio } from "../api/syncPortfolio";
import type { PortfolioResponse } from "../contracts/PortfolioResponse";

export function usePortfolio() {
  const [data, setData] = useState<PortfolioResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const polling = useRef(false);
  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const next = await getPortfolio(signal);
      setData(next);
      setError(next.error);
    } catch (cause) {
      if (!(cause instanceof Error && cause.name === "AbortError")) {
        setError(
          cause instanceof Error ? cause.message : "Unable to load portfolio.",
        );
      }
    }
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    void Promise.resolve().then(() => {
      if (!controller.signal.aborted) {
        return load(controller.signal);
      }
      return undefined;
    });
    return () => {
      controller.abort();
    };
  }, [load]);
  useEffect(() => {
    if (!data?.syncing) {
      return;
    }
    const controller = new AbortController();
    const timer = window.setInterval(() => {
      if (polling.current) {
        return;
      }
      polling.current = true;
      void load(controller.signal).finally(() => {
        polling.current = false;
      });
    }, 2000);
    return () => {
      window.clearInterval(timer);
      controller.abort();
    };
  }, [data?.syncing, load]);
  const refresh = async () => {
    setRefreshing(true);
    try {
      await syncPortfolio();
      await load();
    } catch (cause) {
      setError(
        cause instanceof Error ? cause.message : "Unable to sync Bitkub data.",
      );
    } finally {
      setRefreshing(false);
    }
  };
  return { data, error, refreshing, load, refresh };
}
