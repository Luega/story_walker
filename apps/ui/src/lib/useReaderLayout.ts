"use client";

import { useEffect, useState } from "react";

// Only a desktop - a wide screen driven by a mouse - shows two facing pages, as in a bound book.
// Phones and tablets, in either orientation, show one page at a time and the reader takes over the
// whole screen (ADR-0033). The pointer, not the width alone, is what tells a tablet in landscape
// from a laptop.
const DESKTOP = "(min-width: 1024px) and (hover: hover) and (pointer: fine)";

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
  const desktop = useMediaQuery(DESKTOP);
  return { pagesPerView: desktop ? 2 : 1, immersive: !desktop };
}
