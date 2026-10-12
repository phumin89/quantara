import { NextRequest, NextResponse } from "next/server";
import { proxyCrypto } from "../proxy";
export function POST(request: NextRequest) {
  const origin = request.headers.get("origin");
  if (origin && origin !== request.nextUrl.origin) {
    return NextResponse.json(
      { error: "Cross-origin sync is not allowed." },
      { status: 403 },
    );
  }
  return proxyCrypto("sync", "POST");
}
