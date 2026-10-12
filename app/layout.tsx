import type { Metadata } from "next";
import Providers from "./providers";
import AppShell from "./app-shell";
import "./globals.css";

export const metadata: Metadata = {
  title: "Quantara",
  description: "Quantara AI Company",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>
        <Providers>
          <AppShell>{children}</AppShell>
        </Providers>
      </body>
    </html>
  );
}
