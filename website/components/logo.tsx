/** A double helix: two strands crossing twice, with the rungs between them. */
export function LogoMark({ className = "size-7" }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" className={className} aria-hidden>
      <defs>
        <linearGradient id="helix-mark" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#ff4d67" />
          <stop offset=".5" stopColor="#f0193d" />
          <stop offset="1" stopColor="#9e1028" />
        </linearGradient>
      </defs>
      <g stroke="url(#helix-mark)" strokeLinecap="round" fill="none">
        <path d="M13.4 5.6h5.2M13.6 13h4.8M13.6 19h4.8M13.4 26.4h5.2" strokeWidth="2" opacity=".5" />
        <path d="M22 3c0 6.5-12 6.5-12 13s12 6.5 12 13" strokeWidth="3.4" opacity=".6" />
        <path d="M10 3c0 6.5 12 6.5 12 13S10 22.5 10 29" strokeWidth="3.4" />
      </g>
    </svg>
  );
}

export function Logo() {
  return (
    <span className="flex items-center gap-2.5">
      <LogoMark />
      <span className="font-mono text-lg font-semibold tracking-tight">helix</span>
    </span>
  );
}
