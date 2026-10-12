import {
  portfolioSchema,
  type PortfolioResponse,
} from "../contracts/PortfolioResponse";
import readJson from "../../../shared/api/readJson";
import errorMessage from "../../../shared/api/errorMessage";

export async function getPortfolio(
  signal?: AbortSignal,
): Promise<PortfolioResponse> {
  const response = await fetch("/api/crypto/portfolio", {
    cache: "no-store",
    ...(signal ? { signal } : {}),
  });
  const body = await readJson(response);
  if (!response.ok) {
    throw new Error(errorMessage(body, "Unable to load portfolio."));
  }
  const parsed = portfolioSchema.safeParse(body);
  if (!parsed.success) {
    throw new Error("Portfolio response is invalid.");
  }
  return parsed.data;
}
