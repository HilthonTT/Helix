export type DocLink = { title: string; href: string };
export type DocSection = { title: string; links: DocLink[] };

/** Order of the docs sidebar and the previous/next links. */
export const docs: DocSection[] = [
  {
    title: "Getting started",
    links: [
      { title: "Introduction", href: "/docs/" },
      { title: "Installation", href: "/docs/installation/" },
      { title: "Quick start", href: "/docs/quick-start/" },
    ],
  },
  {
    title: "Using Helix",
    links: [
      { title: "The dashboard", href: "/docs/dashboard/" },
      { title: "Adding drives", href: "/docs/adding-drives/" },
      { title: "Diagnosing a drive", href: "/docs/diagnosing/" },
      { title: "Groups & selections", href: "/docs/groups/" },
      { title: "Schedules", href: "/docs/schedules/" },
      { title: "Away from home", href: "/docs/away-from-home/" },
      { title: "Tray & shortcuts", href: "/docs/tray-and-shortcuts/" },
      { title: "Command line", href: "/docs/command-line/" },
      { title: "Import & export", href: "/docs/import-export/" },
      { title: "Settings & themes", href: "/docs/settings/" },
    ],
  },
  {
    title: "Reference",
    links: [
      { title: "Security", href: "/docs/security/" },
      { title: "Platform support", href: "/docs/platforms/" },
      { title: "Troubleshooting", href: "/docs/troubleshooting/" },
    ],
  },
  {
    title: "Contributing",
    links: [
      { title: "Architecture", href: "/docs/architecture/" },
      { title: "Development", href: "/docs/development/" },
      { title: "Releasing", href: "/docs/releasing/" },
      { title: "Recording media", href: "/docs/media/" },
    ],
  },
];

export const allDocs: DocLink[] = docs.flatMap((s) => s.links);
