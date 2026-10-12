import type { Metadata } from "next";
import Providers from "../shared/theme/Providers";
import AppShell from "../shared/layout/AppShell";
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
