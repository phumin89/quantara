import { NextRequest, NextResponse } from "next/server";
import { proxyCrypto } from "../proxy";
export const dynamic = "force-dynamic";
export function GET(request: NextRequest) {
  const coin = request.nextUrl.searchParams.get("coin");
  if (!coin || !/^[a-zA-Z0-9]{1,30}$/.test(coin)) {
    return NextResponse.json(
      { error: "Invalid coin symbol." },
      { status: 400 },
    );
  }
  return proxyCrypto(`history?coin=${encodeURIComponent(coin)}`);
}
