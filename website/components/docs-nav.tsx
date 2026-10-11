"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { ArrowLeft, ArrowRight } from "lucide-react";

import { allDocs, docs } from "@/lib/docs";

function useCurrent() {
  const path = usePathname();
  return path.endsWith("/") ? path : `${path}/`;
}

export function DocsSidebar() {
  const current = useCurrent();
  return (
    <nav className="space-y-8 text-sm">
      {docs.map((section) => (
        <div key={section.title}>
          <p className="mb-2 px-3 font-mono text-xs uppercase tracking-widest text-mist-400">
            {section.title}
          </p>
          <ul className="space-y-0.5">
            {section.links.map((link) => {
              const active = current === link.href;
              return (
                <li key={link.href}>
                  <Link
                    href={link.href}
                    className={`block rounded-lg px-3 py-1.5 transition ${
                      active
                        ? "bg-signal/10 font-medium text-signal"
                        : "text-mist-300 hover:bg-white/5 hover:text-white"
                    }`}
                  >
                    {link.title}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}

export function DocsPager() {
  const current = useCurrent();
  const index = allDocs.findIndex((d) => d.href === current);
  const prev = index > 0 ? allDocs[index - 1] : undefined;
  const next =
    index >= 0 && index < allDocs.length - 1 ? allDocs[index + 1] : undefined;
  return (
    <div className="mt-16 grid gap-4 border-t border-white/5 pt-8 sm:grid-cols-2">
      {prev ? (
        <Link
          href={prev.href}
          className="group rounded-xl border border-white/10 p-4 transition hover:border-white/25"
        >
          <span className="flex items-center gap-1 text-xs text-mist-400">
            <ArrowLeft className="size-3" /> Previous
          </span>
          <span className="mt-1 block font-medium text-white">
            {prev.title}
          </span>
        </Link>
      ) : (
        <span />
      )}
      {next && (
        <Link
          href={next.href}
          className="group rounded-xl border border-white/10 p-4 text-right transition hover:border-white/25"
        >
          <span className="flex items-center justify-end gap-1 text-xs text-mist-400">
            Next <ArrowRight className="size-3" />
          </span>
          <span className="mt-1 block font-medium text-white">
            {next.title}
          </span>
        </Link>
      )}
    </div>
  );
}
