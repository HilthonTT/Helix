"use client";

import { useState } from "react";
import { Check, Copy } from "lucide-react";

export function CopyCommand({ command }: { command: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="group flex w-full max-w-3xl items-center gap-3 rounded-xl border border-white/10 bg-ink-950/80 py-2 pr-2 pl-4 font-mono text-[0.8rem] shadow-lg sm:text-sm shadow-black/30">
      <span className="select-none text-signal">$</span>
      <code className="flex-1 overflow-x-auto whitespace-nowrap text-left text-mist-100 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
        {command}
      </code>
      <button
        type="button"
        aria-label="Copy command"
        onClick={async () => {
          await navigator.clipboard.writeText(command);
          setCopied(true);
          setTimeout(() => setCopied(false), 1500);
        }}
        className="rounded-lg p-2 text-mist-400 transition hover:bg-white/5 hover:text-white"
      >
        {copied ? (
          <Check className="size-4 text-success" />
        ) : (
          <Copy className="size-4" />
        )}
      </button>
    </div>
  );
}
