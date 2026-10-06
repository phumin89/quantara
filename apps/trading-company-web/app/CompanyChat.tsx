"use client";

import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { CompanyConversation } from "./companyConversationView";

export function CompanyChat({ api, children, compact = false }: { api?: string; children?: ReactNode; compact?: boolean }) {
  const [open, setOpen] = useState(false);
  const [minimized, setMinimized] = useState(false);
  const launcherRef = useRef<HTMLButtonElement>(null);
  const minimizeRef = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(false);

  useEffect(() => {
    if (open) minimizeRef.current?.focus();
    else if (wasOpen.current) launcherRef.current?.focus();
    wasOpen.current = open;
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || !(event.target instanceof Node) || !document.getElementById("company-team-chat")?.contains(event.target)) return;
      setOpen(false);
      setMinimized(true);
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open]);

  const openChat = () => {
    setOpen(true);
    setMinimized(false);
  };
  return <>
    <button
      ref={launcherRef}
      type="button"
      className="team-chat-launcher"
      hidden={open}
      aria-controls="company-team-chat"
      aria-expanded={open}
      aria-label={minimized ? "เปิดแชตทีมต่อ" : "แชตทีม"}
      onClick={openChat}
    >
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 11a8 8 0 0 1-8 8H5l-3 3V11a9 9 0 0 1 18 0Z" /><path d="M7 10h10M7 14h6" /></svg>
      <span className="team-chat-launcher-label">{minimized ? "เปิดแชตทีมต่อ" : "แชตทีม"}</span>
    </button>
    <section
      id="company-team-chat"
      className="team-chat-panel"
      data-compact={compact}
      role="dialog"
      aria-labelledby="company-team-chat-title"
      hidden={!open}
    >
      <header className="team-chat-header">
        <h2 id="company-team-chat-title" className="team-chat-title">ห้องทีม</h2>
        <div className="team-chat-actions">
          <button ref={minimizeRef} type="button" aria-label="ย่อแชตทีม" title="ย่อแชตทีม" onClick={() => { setMinimized(true); setOpen(false); }}>
            <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="2"><path d="M5 12h14" /></svg>
          </button>
        </div>
      </header>
      <div className="team-chat-content">
        {children ?? (api ? <CompanyConversation api={api} /> : null)}
      </div>
    </section>
  </>;
}
