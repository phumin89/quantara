"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { LiveConnectionBadge } from "./LiveAccountPanel";
const links = [["/", "สำนักงาน", "M3 21h18M5 21V7l7-4 7 4v14M9 21v-6h6v6M9 9h.01M15 9h.01"], ["/account", "บัญชีและสินทรัพย์", "M3 6h16v3H5a2 2 0 0 1-2-2Zm0 1v12h18V9H5M16 14h3"], ["/trading", "การเทรด", "M3 3v18h18M6 15l5-5 4 3 6-8M16 5h5v5"]] as const;
export function AppShell({ children }: { children: ReactNode }) {
  const pathname = usePathname(); const current = links.find(([href]) => href === pathname)?.[1] ?? "สำนักงาน";
  return <div className="app-shell">
    <a className="skip-link" href="#main-content">ข้ามไปยังเนื้อหาหลัก</a>
    <aside className="app-sidebar" aria-label="การนำทางหลัก">
      <Link className="app-brand" href="/">Quantara</Link>
      <nav className="app-nav">{links.map(([href, label, icon]) =>
        <Link key={href} className={pathname === href ? "selected" : ""} aria-current={pathname === href ? "page" : undefined} href={href}>
          <svg className="app-nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d={icon} /></svg>{label}
        </Link>)}</nav>
      <div className="app-sidebar-status">Live · Bitkub</div>
    </aside>
    <div className="app-content">
      <header className="app-header"><h1>{current}</h1><LiveConnectionBadge /></header>
      <main id="main-content" className="app-main" tabIndex={-1}>{children}</main>
    </div>
  </div>;
}
