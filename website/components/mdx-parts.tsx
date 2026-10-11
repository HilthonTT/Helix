import { Info, TriangleAlert } from "lucide-react";

export function Callout({ type = "info", children }: { type?: "info" | "warn"; children: React.ReactNode }) {
  const warn = type === "warn";
  const Icon = warn ? TriangleAlert : Info;
  return (
    <div
      className={`not-prose my-6 flex gap-3 rounded-xl border p-4 text-sm leading-relaxed ${
        warn ? "border-warning/30 bg-warning/5 text-mist-100" : "border-signal-soft/30 bg-signal-soft/5 text-mist-100"
      }`}
    >
      <Icon className={`mt-0.5 size-4 shrink-0 ${warn ? "text-warning" : "text-signal-soft"}`} />
      <div className="[&_code]:rounded [&_code]:bg-white/10 [&_code]:px-1 [&_code]:font-mono">{children}</div>
    </div>
  );
}

export function Kbd({ children }: { children: React.ReactNode }) {
  return (
    <kbd className="rounded-md border border-white/15 border-b-white/25 bg-ink-800 px-1.5 py-0.5 font-mono text-[0.8em] text-mist-100">
      {children}
    </kbd>
  );
}
