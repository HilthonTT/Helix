import { asset } from "@/lib/site";

type MediaProps = {
  src: string;
  alt: string;
  caption?: string;
  className?: string;
  priority?: boolean;
};

/** A screenshot or GIF of Helix, recorded with `website/scripts/record-media.py`. */
export function Media({ src, alt, caption, className = "", priority }: MediaProps) {
  const img = (
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={asset(src.startsWith("/") ? src : `/media/${src}`)}
      alt={alt}
      loading={priority ? "eager" : "lazy"}
      className={`w-full rounded-2xl shadow-2xl ring-1 shadow-black/50 ring-white/10 ${className}`}
    />
  );
  if (!caption) return img;
  return (
    <figure className="not-prose my-8">
      {img}
      <figcaption className="mt-3 text-center text-sm text-mist-400">{caption}</figcaption>
    </figure>
  );
}
