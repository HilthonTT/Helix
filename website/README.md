# Helix website

The documentation site for Helix, built with Next.js (App Router, static export), Tailwind CSS and MDX.

```sh
npm install
npm run dev        # http://localhost:3000
npm run build      # static site in out/
```

- Pages live in `app/`; documentation pages are MDX files in `app/docs/<page>/page.mdx`.
- The docs sidebar order is in `lib/docs.ts`.
- GIFs and screenshots in `public/media/` are recorded from the real app by `scripts/record-media.py` (Windows; see the "Recording media" page).
- Pushing to `main` deploys to GitHub Pages through `.github/workflows/website.yml`. Set `NEXT_PUBLIC_BASE_PATH` when serving from a sub-path.
