import { proxyCrypto } from "../proxy";

export const dynamic = "force-dynamic";
export function GET() {
  return proxyCrypto("portfolio");
}
