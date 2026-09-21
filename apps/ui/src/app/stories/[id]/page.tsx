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
/** How long the controls linger over the artwork before fading away. */
const CONTROLS_IDLE_MS = 3000;

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
 * Below desktop the reader takes over the screen: the pages are the whole page, and the controls
 * float over them in the same frosted glass as the story text, then get out of the way.
 */
export default function StoryPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [firstPage, setFirstPage] = useState(0);
  const [controlsShown, setControlsShown] = useState(true);
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
    setControlsShown(true);
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

  // Reading is the point, so the controls step back once the page has settled. Any tap brings them
  // round again.
  useEffect(() => {
    if (!immersive || !controlsShown) return;
    const timer = setTimeout(() => setControlsShown(false), CONTROLS_IDLE_MS);
    return () => clearTimeout(timer);
  }, [immersive, controlsShown, start]);

  // Turning a page by hand: a flick either way turns it, anything shorter is a tap on the paper.
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
    if (travelled <= -SWIPE_PX) {
      if (!atEnd) turn(1);
    } else if (travelled >= SWIPE_PX) {
      if (!atStart) turn(-1);
    } else {
      setControlsShown((shown) => !shown);
    }
  }

  if (state.kind === "loading") return <p className="text-muted">Loading…</p>;

  if (state.kind === "notFound" || state.kind === "error") {
    return (
      <div className="space-y-4 py-16 text-center">
        <p className="font-display text-2xl">
          {state.kind === "notFound" ? "Story not found." : state.message}
        </p>
        <Link href="/dashboard" className="font-semibold text-accent hover:underline">
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

  return (
    <section
      role="region"
      aria-label="Story reader"
      onPointerDown={onPointerDown}
      onPointerUp={onPointerUp}
      className={
        immersive
          ? "fixed inset-0 z-50 touch-pan-y select-none overscroll-contain bg-background"
          : "space-y-6"
      }
    >
      {immersive ? (
        <>
          <PageSpread pages={visible} immersive />

          <nav
            aria-label="Reader controls"
            data-reader-controls
            inert={!controlsShown}
            className={`pointer-events-none absolute inset-0 flex flex-col justify-between transition-opacity duration-500 motion-reduce:transition-none ${
              controlsShown ? "opacity-100" : "opacity-0"
            }`}
          >
            <div className="pointer-events-auto flex items-center gap-3 border-b border-glass-edge bg-glass px-4 pt-[calc(env(safe-area-inset-top)+0.75rem)] pb-3 backdrop-blur-xl backdrop-saturate-150">
              <Link
                href="/dashboard"
                aria-label="Back to your stories"
                className="-m-2 p-2 text-xl leading-none text-muted"
              >
                ←
              </Link>
              <h1 className="truncate font-display text-lg font-semibold tracking-tight">
                {s.title}
              </h1>
            </div>

            <div className="pointer-events-auto flex items-center justify-between gap-4 border-t border-glass-edge bg-glass px-4 pt-3 pb-[calc(env(safe-area-inset-bottom)+0.75rem)] backdrop-blur-xl backdrop-saturate-150">
              <button
                type="button"
                aria-label="Previous page"
                onClick={() => turn(-1)}
                disabled={atStart}
                className="-m-2 p-2 text-xl leading-none disabled:opacity-30"
              >
                ←
              </button>
              <span className="text-sm text-muted">{position}</span>
              <button
                type="button"
                aria-label="Next page"
                onClick={() => turn(1)}
                disabled={atEnd}
                className="-m-2 p-2 text-xl leading-none disabled:opacity-30"
              >
                →
              </button>
            </div>
          </nav>
        </>
      ) : (
        <>
          <Link href="/dashboard" className="text-sm font-semibold text-muted hover:text-accent">
            ← All stories
          </Link>

          <div className="space-y-2">
            <h1 className="font-display text-4xl font-semibold tracking-tight">{s.title}</h1>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted">
              <span>Age {s.targetAge}+</span>
              <span aria-hidden>·</span>
              <span>{s.artStyle}</span>
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
                  className="rounded-xl border border-border bg-surface px-4 py-2 font-semibold disabled:opacity-40"
                >
                  ← Previous
                </button>
                <span className="text-sm text-muted">{position}</span>
                <button
                  type="button"
                  onClick={() => turn(1)}
                  disabled={atEnd}
                  className="rounded-xl border border-border bg-surface px-4 py-2 font-semibold disabled:opacity-40"
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
