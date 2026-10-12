import { historyResponseSchema } from "../contracts/HistoryResponse";
import readJson from "../../../shared/api/readJson";
import errorMessage from "../../../shared/api/errorMessage";

export async function getTradeHistory(coin: string, signal?: AbortSignal) {
  const response = await fetch(
    `/api/crypto/history?coin=${encodeURIComponent(coin)}`,
    { cache: "no-store", ...(signal ? { signal } : {}) },
  );
  const body = await readJson(response);
  if (!response.ok) {
    throw new Error(errorMessage(body, "Unable to load history."));
  }
  const parsed = historyResponseSchema.safeParse(body);
  if (!parsed.success) {
    throw new Error("History response is invalid.");
  }
  return parsed.data;
}
