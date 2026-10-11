export const site = {
  name: "Helix",
  tagline: "Your NAS drives, connected and kept that way",
  repo: "https://github.com/HilthonTT/Helix",
  releases: "https://github.com/HilthonTT/Helix/releases/latest",
  install: "git clone https://github.com/HilthonTT/Helix && cd Helix && dotnet build Helix.slnx",
};

/** Prefixes public assets with the deploy base path (GitHub Pages). */
export function asset(path: string): string {
  const base = process.env.NEXT_PUBLIC_BASE_PATH ?? "";
  return `${base}${path.startsWith("/") ? path : `/${path}`}`;
}
