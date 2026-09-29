"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { getStory, type Story } from "@/lib/api";
import { PageSpread } from "@/components/PageSpread";
import { StatusBadge } from "@/components/StatusBadge";
import { useReaderLayout } from "@/lib/useReaderLayout";

/** How far a finger has to travel before it counts as turning the page rather than a tap. */
const SWIPE_PX = 60;

type LoadState =
  | { kind: "loading" }
  | { kind: "notFound" }
  | { kind: "error"; message: string }
  | { kind: "loaded"; story: Story };

/**
 * Only a Complete story can be read here. A story still being generated has nothing to show, and a
 * failed one never will, so both are sent back to the dashboard - which is where their state is
 * explained. The status is settled once and for all by the time it reaches Complete, so there is
 * nothing on this page left to poll for.
 *
 * Below desktop the reader takes over the screen and keeps to the essentials (ADR-0033): a way
 * back, the title, one page, and two big buttons to turn it. They stay put, so a child always
 * knows how to get to the next page.
 */
export default function StoryPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [firstPage, setFirstPage] = useState(0);
  const { pagesPerView, immersive: fullScreen } = useReaderLayout();

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const story = await getStory(id);
        if (cancelled) return;
        if (!story) {
          setState({ kind: "notFound" });
          return;
        }
        if (story.status !== "Complete") {
          // replace, not push: Back should return where the reader came from, not here.
          router.replace("/dashboard");
          return;
        }
        setState({ kind: "loaded", story });
      } catch (e) {
        if (!cancelled) setState({ kind: "error", message: (e as Error).message });
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [id, router]);

  const story = state.kind === "loaded" ? state.story : null;
  const pages = story?.pages ?? [];
  const immersive = fullScreen && pages.length > 0;

  // A spread always starts on an odd page, the way a book opens; a single page moves one at a time.
  const last = Math.max(0, pages.length - 1);
  const start = Math.min(pagesPerView === 2 ? Math.floor(firstPage / 2) * 2 : firstPage, last);
  const visible = pages.slice(start, start + pagesPerView);
  const atStart = start === 0;
  const atEnd = start + pagesPerView >= pages.length;

  function turn(direction: -1 | 1) {
    setFirstPage(Math.max(0, Math.min(start + direction * pagesPerView, last)));
  }

  useEffect(() => {
    function onKey(e: globalThis.KeyboardEvent) {
      if (e.key === "ArrowLeft" && !atStart) turn(-1);
      if (e.key === "ArrowRight" && !atEnd) turn(1);
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // `turn` reads the page it is turning from, so the listener is renewed whenever that moves.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [start, pagesPerView, last, atStart, atEnd]);

  // Turning a page by hand: a flick either way turns it, anything shorter is just a tap.
  const swipeFrom = useRef<number | null>(null);
  const onControls = (e: React.PointerEvent) =>
    e.target instanceof Element && e.target.closest("[data-reader-controls]") !== null;

  function onPointerDown(e: React.PointerEvent) {
    swipeFrom.current = immersive && !onControls(e) ? e.clientX : null;
  }

  function onPointerUp(e: React.PointerEvent) {
    const from = swipeFrom.current;
    swipeFrom.current = null;
    if (from === null || onControls(e)) return;

    const travelled = e.clientX - from;
    if (travelled <= -SWIPE_PX && !atEnd) turn(1);
    if (travelled >= SWIPE_PX && !atStart) turn(-1);
  }

  if (state.kind === "loading") return <p className="text-muted">Loading…</p>;

  if (state.kind === "notFound" || state.kind === "error") {
    return (
      <div className="space-y-4 py-16 text-center">
        <p className="font-display text-2xl">
          {state.kind === "notFound" ? "Story not found." : state.message}
        </p>
        <Link href="/dashboard" className="btn-secondary">
          ← Back to your stories
        </Link>
      </div>
    );
  }

  const { story: s } = state;
  const firstVisible = start + 1;
  const lastVisible = start + visible.length;
  const position =
    firstVisible === lastVisible
      ? `Page ${firstVisible} of ${pages.length}`
      : `Pages ${firstVisible}–${lastVisible} of ${pages.length}`;

  const dots = (
    <PageDots count={pages.length} first={start} shown={visible.length} />
  );

  return (
    <section
      role="region"
      aria-label="Story reader"
      onPointerDown={onPointerDown}
      onPointerUp={onPointerUp}
      className={
        immersive
          ? "fixed inset-0 z-50 flex touch-pan-y flex-col overscroll-contain bg-background select-none"
          : "space-y-6"
      }
    >
      {immersive ? (
        <>
          <div className="flex items-center gap-2 px-3 pt-[calc(env(safe-area-inset-top)+0.75rem)]">
            <Link href="/dashboard" aria-label="Back to your stories" className="btn-icon">
              ←
            </Link>
            <h1 className="truncate font-display text-lg font-semibold tracking-tight">
              {s.title}
            </h1>
          </div>

          <div className="min-h-0 flex-1 px-4 py-3">
            <PageSpread pages={visible} immersive />
          </div>

          <nav
            aria-label="Reader controls"
            data-reader-controls
            className="flex items-center justify-between gap-4 px-6 pt-1 pb-[calc(env(safe-area-inset-bottom)+1rem)]"
          >
            <button
              type="button"
              aria-label="Previous page"
              onClick={() => turn(-1)}
              disabled={atStart}
              className="btn-primary size-16 p-0 text-3xl"
            >
              ←
            </button>
            {dots}
            <span className="sr-only">{position}</span>
            <button
              type="button"
              aria-label="Next page"
              onClick={() => turn(1)}
              disabled={atEnd}
              className="btn-primary size-16 p-0 text-3xl"
            >
              →
            </button>
          </nav>
        </>
      ) : (
        <>
          <Link
            href="/dashboard"
            className="inline-flex rounded-full px-3 py-1.5 text-sm font-bold text-muted transition hover:bg-lilac-soft hover:text-accent"
          >
            ← All stories
          </Link>

          <div className="space-y-3">
            <h1 className="font-display text-4xl font-semibold tracking-tight">{s.title}</h1>
            <div className="flex flex-wrap items-center gap-2">
              <span className="chip bg-sky-soft">Age {s.targetAge}+</span>
              <span className="chip bg-peach-soft">{s.artStyle}</span>
              <StatusBadge status={s.status} />
            </div>
          </div>

          {pages.length > 0 && (
            <>
              <PageSpread pages={visible} immersive={false} />

              <nav className="flex items-center justify-between gap-4">
                <button
                  type="button"
                  onClick={() => turn(-1)}
                  disabled={atStart}
                  className="btn-secondary"
                >
                  ← Previous
                </button>
                <div className="flex flex-col items-center gap-2">
                  {dots}
                  <span className="text-sm text-muted">{position}</span>
                </div>
                <button
                  type="button"
                  onClick={() => turn(1)}
                  disabled={atEnd}
                  className="btn-secondary"
                >
                  Next →
                </button>
              </nav>
            </>
          )}
        </>
      )}
    </section>
  );
}

/** One dot per page, the ones on screen drawn long, so a child can see how far the story has come. */
function PageDots({ count, first, shown }: { count: number; first: number; shown: number }) {
  return (
    <ol aria-hidden className="flex flex-wrap items-center justify-center gap-1.5">
      {Array.from({ length: count }, (_, i) => (
        <li
          key={i}
          className={`h-2.5 rounded-full transition-all ${
            i >= first && i < first + shown ? "w-6 bg-accent" : "w-2.5 bg-border"
          }`}
        />
      ))}
    </ol>
  );
}
