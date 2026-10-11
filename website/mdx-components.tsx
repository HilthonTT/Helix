import type { MDXComponents } from "mdx/types";
import Link from "next/link";

import { Media } from "@/components/media";
import { Callout, Kbd } from "@/components/mdx-parts";

/** Markdown links within the site go through Link, which adds the deploy base path. */
function Anchor({ href = "", ...props }: React.ComponentProps<"a">) {
  if (href.startsWith("/")) {
    return <Link href={href} {...props} />;
  }

  return <a href={href} {...props} />;
}

export function useMDXComponents(components: MDXComponents): MDXComponents {
  return { ...components, a: Anchor, Media, Callout, Kbd };
}
