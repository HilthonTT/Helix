import Link from "next/link";
import {
  AlarmClock,
  ArrowRight,
  BellRing,
  Download,
  FolderSync,
  Globe,
  KeyRound,
  Languages,
  Layers,
  LockKeyhole,
  Palette,
  Power,
  Stethoscope,
  Terminal,
  Wifi,
} from "lucide-react";

import { CopyCommand } from "@/components/copy-command";
import { GithubIcon } from "@/components/github-icon";
import { Media } from "@/components/media";
import { ScreenGallery } from "@/components/screen-gallery";
import { SiteFooter } from "@/components/site-footer";
import { SiteHeader } from "@/components/site-header";
import { site } from "@/lib/site";

const haze = ["#ff4d67", "#f0193d", "#c4142f", "#8a1024", "#620e1c"];

/** Soft drifting bands behind the hero. */
function Haze() {
  return (
    <div
      aria-hidden
      className="pointer-events-none absolute inset-x-0 top-0 -z-10 h-[56rem] overflow-hidden [mask-image:linear-gradient(to_bottom,black_45%,transparent)]"
    >
      <div className="bg-grid absolute inset-0" />
      {haze.map((c, i) => (
        <div
          key={c}
          className="absolute left-1/2 h-40 w-[140%] -translate-x-1/2 rounded-[100%] blur-3xl"
          style={{
            top: `${6 + i * 7}rem`,
            background: c,
            opacity: 0.07 + (4 - i) * 0.012,
            transform: `translateX(-50%) rotate(${-4 + i * 2}deg)`,
          }}
        />
      ))}
    </div>
  );
}

function Section({
  id,
  eyebrow,
  title,
  intro,
  children,
}: {
  id?: string;
  eyebrow: string;
  title: React.ReactNode;
  intro?: string;
  children: React.ReactNode;
}) {
  return (
    <section id={id} className="mx-auto max-w-7xl scroll-mt-24 px-5 py-24">
      <p className="font-mono text-xs uppercase tracking-[0.25em] text-signal">{eyebrow}</p>
      <h2 className="mt-3 max-w-3xl text-3xl font-semibold tracking-tight text-white sm:text-4xl">{title}</h2>
      {intro && <p className="mt-4 max-w-2xl text-lg leading-relaxed text-mist-300">{intro}</p>}
      <div className="mt-12">{children}</div>
    </section>
  );
}

function Showcase({
  title,
  text,
  media,
  alt,
  href,
  icon: Icon,
}: {
  title: string;
  text: string;
  media: string;
  alt: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
}) {
  return (
    <div className="group flex flex-col overflow-hidden rounded-3xl border border-white/[0.07] bg-ink-850">
      <div className="p-7 pb-0">
        <span className="inline-flex rounded-xl bg-white/5 p-2.5 text-signal-soft ring-1 ring-white/10">
          <Icon className="size-5" />
        </span>
        <h3 className="mt-4 text-lg font-semibold text-white">{title}</h3>
        <p className="mt-1.5 text-sm leading-relaxed text-mist-300">{text}</p>
        <Link href={href} className="mt-3 inline-flex items-center gap-1.5 text-sm font-medium text-signal-soft hover:underline">
          Read more <ArrowRight className="size-3.5" />
        </Link>
      </div>
      <div className="mt-6 flex-1 px-3 pb-3">
        <Media src={media} alt={alt} className="transition duration-500 group-hover:scale-[1.01]" />
      </div>
    </div>
  );
}

const capabilities = [
  {
    icon: Wifi,
    title: "Reconnects on its own",
    text: "A watchdog brings dropped shares back, and catches up the moment the laptop wakes.",
  },
  {
    icon: Power,
    title: "Wakes a sleeping NAS",
    text: "Store its hardware address and Helix sends a Wake-on-LAN packet before giving up.",
  },
  {
    icon: Globe,
    title: "Away from home",
    text: "Pin a drive to its network, or give it a VPN address to use everywhere else.",
  },
  {
    icon: AlarmClock,
    title: "Schedules",
    text: "Connect Office at 07:00 on weekdays, take everything down at 23:00.",
  },
  {
    icon: BellRing,
    title: "Lives in the tray",
    text: "Connect, disconnect and open drives from the tray, with a toast when one drops.",
  },
  {
    icon: Terminal,
    title: "Scriptable",
    text: "--connect-all, --wake Z, --status: drive the running app from Task Scheduler.",
  },
  {
    icon: KeyRound,
    title: "Encrypted at rest",
    text: "SQLCipher database, PBKDF2 passwords, a recovery key, AES-256-GCM vault exports.",
  },
  {
    icon: Languages,
    title: "Six languages",
    text: "English, French, German, Dutch, Indonesian and Japanese, switched live.",
  },
];

export default function Home() {
  return (
    <>
      <SiteHeader />
      <main className="relative isolate">
        <Haze />

        {/* Hero */}
        <section className="mx-auto max-w-7xl px-5 pt-20 pb-12 text-center sm:pt-28">
          <a
            href={site.releases}
            className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.03] px-4 py-1.5 text-xs text-mist-300 transition hover:border-white/25 hover:text-white"
          >
            <span className="size-1.5 rounded-full bg-success" />
            Windows 10+ and macOS 15+ · download the latest release
            <ArrowRight className="size-3" />
          </a>
          <h1 className="mx-auto mt-8 max-w-4xl text-5xl font-semibold tracking-tight text-white sm:text-7xl">
            Your NAS drives, <span className="text-crimson">connected and kept that way.</span>
          </h1>
          <p className="mx-auto mt-6 max-w-2xl text-lg leading-relaxed text-mist-300 sm:text-xl">
            Helix maps network shares to drive letters, watches them, and brings them back when they drop: after sleep,
            on a new network, or when the NAS wakes up. When one will not connect, it tells you why.
          </p>
          <div className="mt-10 flex flex-col items-center gap-4">
            <div className="flex flex-wrap justify-center gap-3">
              <a
                href={site.releases}
                className="inline-flex items-center gap-2 rounded-xl bg-white px-5 py-2.5 text-sm font-semibold text-ink-900 transition hover:bg-mist-100"
              >
                <Download className="size-4" /> Download
              </a>
              <Link
                href="/docs/quick-start/"
                className="inline-flex items-center gap-2 rounded-xl border border-white/10 px-5 py-2.5 text-sm font-semibold text-white transition hover:border-white/30"
              >
                Get started <ArrowRight className="size-4" />
              </Link>
              <a
                href={site.repo}
                className="inline-flex items-center gap-2 rounded-xl border border-white/10 px-5 py-2.5 text-sm font-semibold text-white transition hover:border-white/30"
              >
                <GithubIcon /> Star on GitHub
              </a>
            </div>
          </div>
        </section>

        <div className="relative mx-auto max-w-6xl px-5">
          <div
            aria-hidden
            className="absolute inset-x-16 top-10 -z-10 h-[80%] rounded-full bg-gradient-to-r from-signal/25 via-signal/20 to-signal-soft/25 blur-3xl"
          />
          <Media
            src="overview.gif"
            alt="Signing in to Helix, then visiting the dashboard, the audit log and the settings"
            priority
          />
        </div>

        {/* Showcase */}
        <Section
          id="features"
          eyebrow="Features"
          title={
            <>
              Built for the drives <span className="text-crimson">you actually have</span>.
            </>
          }
          intro="Thirteen shares on one NAS, a laptop that leaves the house, a server that sleeps at night. Helix is written around that, on the Win32 WNet API on Windows and NetFS on macOS."
        >
          <div className="grid gap-5 lg:grid-cols-2">
            <Showcase
              icon={FolderSync}
              title="Add a drive in seconds"
              text="Pick a free letter, type the server and the share, test it without mounting anything, save."
              media="add-drive.gif"
              alt="Filling in the add-drive sheet and saving a new drive"
              href="/docs/adding-drives/"
            />
            <Showcase
              icon={Stethoscope}
              title="Know why it will not connect"
              text="The connection check walks the name, the server, the share and the letter, and says which step failed."
              media="diagnose.gif"
              alt="Running the connection check on a drive whose server does not answer"
              href="/docs/diagnosing/"
            />
            <Showcase
              icon={Layers}
              title="Act on several at once"
              text="Tick rows for a one-off selection, or save groups like Home and Office for the sets you use daily."
              media="selection.gif"
              alt="Ticking three drive rows so the selection bar appears"
              href="/docs/groups/"
            />
            <Showcase
              icon={Palette}
              title="Make it yours"
              text="Crimson by default, plus System, Light, Dark, Midnight, Amethyst, Sand and Glacier."
              media="themes.gif"
              alt="Switching between Helix's themes in Settings"
              href="/docs/settings/"
            />
          </div>

          <div className="mt-5 grid gap-px overflow-hidden rounded-3xl border border-white/[0.07] bg-white/[0.07] sm:grid-cols-2 lg:grid-cols-4">
            {capabilities.map(({ icon: Icon, title, text }) => (
              <div key={title} className="bg-ink-850 p-7">
                <Icon className="size-5 text-signal" />
                <h3 className="mt-4 font-semibold text-white">{title}</h3>
                <p className="mt-1.5 text-sm leading-relaxed text-mist-300">{text}</p>
              </div>
            ))}
          </div>
        </Section>

        {/* Screens */}
        <Section
          id="screens"
          eyebrow="Screens"
          title="One window, the whole estate."
          intro="Every screenshot is recorded from the real app against a demo account whose drives point at documentation addresses."
        >
          <ScreenGallery />
        </Section>

        {/* Security */}
        <Section
          eyebrow="Security"
          title="Credentials that stay where they belong."
          intro="Helix holds the passwords to your NAS, so it treats them that way."
        >
          <div className="grid items-center gap-10 lg:grid-cols-2">
            <pre className="overflow-x-auto rounded-3xl border border-white/[0.07] bg-ink-950 p-7 font-mono text-[0.85rem] leading-7 text-mist-300">
              <code>
                <span className="text-mist-400"># drive the running app from a script</span>
                {"\n"}
                <span className="text-signal">Helix.App.exe</span> --connect-group{" "}
                <span className="text-success">&quot;Office&quot;</span>
                {"\n"}
                <span className="text-signal">Helix.App.exe</span> --wake Z{"\n"}
                <span className="text-signal">Helix.App.exe</span> --status{"\n\n"}
                <span className="text-mist-400"># PowerShell waits only when asked</span>
                {"\n"}
                <span className="text-signal">Start-Process</span> Helix.App.exe -ArgumentList{" "}
                <span className="text-success">&apos;--disconnect-all&apos;</span> -Wait
              </code>
            </pre>
            <ul className="space-y-5 text-mist-300">
              {[
                ["Encrypted database", "SQLite under SQLCipher, keyed from the platform's secure store."],
                ["Never on a command line", "Share passwords go to the OS mount API, where no other process can read them."],
                ["A way back in", "A one-time recovery key resets a forgotten account password."],
                ["Verified updates", "A downloaded release is checked against the digest GitHub publishes before anything is replaced."],
              ].map(([title, text]) => (
                <li key={title} className="flex gap-4">
                  <span className="mt-1.5 size-2 shrink-0 rounded-full bg-gradient-to-br from-signal to-signal-soft" />
                  <span>
                    <span className="font-semibold text-white">{title}.</span> {text}
                  </span>
                </li>
              ))}
              <li>
                <Link href="/docs/security/" className="inline-flex items-center gap-2 font-semibold text-signal-soft hover:underline">
                  <LockKeyhole className="size-4" /> Read the security notes <ArrowRight className="size-4" />
                </Link>
              </li>
            </ul>
          </div>
        </Section>

        {/* CTA */}
        <section className="mx-auto max-w-7xl px-5 pb-28">
          <div className="relative overflow-hidden rounded-3xl border border-white/[0.07] bg-ink-850 px-8 py-16 text-center">
            <div
              aria-hidden
              className="absolute inset-0 -z-0 bg-gradient-to-br from-signal/10 via-transparent to-signal-soft/10"
            />
            <h2 className="relative text-3xl font-semibold tracking-tight text-white sm:text-4xl">
              Unzip it, register, add a drive.
            </h2>
            <p className="relative mx-auto mt-4 max-w-xl text-mist-300">
              Releases are self-contained: nothing to install beside them. Or build it yourself with the .NET 10 SDK and
              the MAUI workload.
            </p>
            <div className="relative mt-8 flex justify-center">
              <CopyCommand command={site.install} />
            </div>
          </div>
        </section>
      </main>
      <SiteFooter />
    </>
  );
}
