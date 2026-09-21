"use client";

import { useEffect, useState } from "react";

// A phone shows one page; from tablet width up, two pages face each other as in a bound book.
const TWO_PAGES = "(min-width: 768px)";
// Below desktop the reader takes over the whole screen, covering the site header.
const DESKTOP = "(min-width: 1024px)";

function useMediaQuery(query: string): boolean {
  // Server-rendered markup never reaches a reader: the story is fetched client-side, so the first
  // paint here is always the "Loading…" state and there is no mismatch to hydrate around.
  const [matches, setMatches] = useState(false);

  useEffect(() => {
    const mql = window.matchMedia(query);
    const sync = () => setMatches(mql.matches);
    sync();
    mql.addEventListener("change", sync);
    return () => mql.removeEventListener("change", sync);
  }, [query]);

  return matches;
}

export interface ReaderLayout {
  /** How many pages are on screen at once. */
  pagesPerView: 1 | 2;
  /** Whether the reader fills the screen and carries its own controls. */
  immersive: boolean;
}

export function useReaderLayout(): ReaderLayout {
  const twoPages = useMediaQuery(TWO_PAGES);
  const desktop = useMediaQuery(DESKTOP);
  return { pagesPerView: twoPages ? 2 : 1, immersive: !desktop };
}
