"use client";

import { useState } from "react";

import { asset } from "@/lib/site";

const screens = [
  { id: "dashboard", label: "Dashboard", file: "dashboard.png", note: "Every drive, its status and the estate" },
  { id: "diagnose", label: "Connection check", file: "diagnose.png", note: "Why a drive will not connect" },
  { id: "groups", label: "Groups", file: "groups.png", note: "Named sets, connected in one click" },
  { id: "audit-log", label: "Audit log", file: "audit-log.png", note: "What happened, and when" },
  { id: "settings", label: "Settings", file: "settings.png", note: "Tray, startup, shortcuts, idle lock" },
  { id: "sign-in", label: "Sign in", file: "sign-in.png", note: "One account per person" },
];

/** A screenshot of each part of the app, picked from a list. */
export function ScreenGallery() {
  const [active, setActive] = useState(screens[0]);
  return (
    <div className="grid gap-8 lg:grid-cols-[17rem_1fr]">
      <div className="flex gap-2 overflow-x-auto pb-2 lg:flex-col lg:overflow-visible lg:pb-0">
        {screens.map((s) => (
          <button
            key={s.id}
            type="button"
            onClick={() => setActive(s)}
            className={`flex shrink-0 flex-col rounded-xl border px-4 py-2.5 text-left text-sm transition ${
              active.id === s.id
                ? "border-signal/50 bg-white/[0.06] text-white"
                : "border-white/5 text-mist-300 hover:border-white/15 hover:text-white"
            }`}
          >
            <span className="font-medium">{s.label}</span>
            <span className="text-xs text-mist-400">{s.note}</span>
          </button>
        ))}
      </div>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        key={active.id}
        src={asset(`/media/${active.file}`)}
        alt={`Helix: ${active.label}`}
        className="w-full rounded-2xl shadow-2xl ring-1 shadow-black/50 ring-white/10"
      />
    </div>
  );
}
