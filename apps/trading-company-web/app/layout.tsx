import type { ReactNode } from "react";
import type { Metadata } from "next";
import { AppShell } from "./AppShell";
import { LiveAccountProvider } from "./LiveAccountPanel";
import "./globals.css";
import "./workspace.css";
export const metadata: Metadata = { title: "Quantara · Live", description: "Quantara Live account readiness with separately authorized trading controls." };
export default function RootLayout({ children }: { children: ReactNode }) { return <html lang="th"><body><LiveAccountProvider><AppShell>{children}</AppShell></LiveAccountProvider></body></html>; }
