import { NextResponse } from "next/server";

export async function proxyCrypto(
  path: string,
  method: "GET" | "POST" = "GET",
) {
  try {
    const response = await fetch(`http://127.0.0.1:5050/api/crypto/${path}`, {
      method,
      cache: "no-store",
      signal: AbortSignal.timeout(10000),
    });
    const body = await response.text();
    return new NextResponse(body, {
      status: response.status,
      headers: {
        "Content-Type": "application/json",
        "Cache-Control": "no-store",
      },
    });
  } catch {
    return NextResponse.json(
      {
        error:
          "The portfolio service is unavailable. Start the C# backend and retry.",
      },
      { status: 503 },
    );
  }
}
