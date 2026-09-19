"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { getStory, type Story } from "@/lib/api";
import { PageSpread } from "@/components/PageSpread";
import { StatusBadge } from "@/components/StatusBadge";

const PAGES_PER_SPREAD = 2;

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
 */
export default function StoryPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [spread, setSpread] = useState(0);

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
  const spreadCount = Math.max(1, Math.ceil(pages.length / PAGES_PER_SPREAD));
  const current = Math.min(spread, spreadCount - 1);
  const visible = pages.slice(current * PAGES_PER_SPREAD, (current + 1) * PAGES_PER_SPREAD);

  useEffect(() => {
    function onKey(e: globalThis.KeyboardEvent) {
      if (e.key === "ArrowLeft") setSpread((s) => Math.max(0, s - 1));
      if (e.key === "ArrowRight") setSpread((s) => Math.min(spreadCount - 1, s + 1));
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [spreadCount]);

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
  const firstVisible = current * PAGES_PER_SPREAD + 1;
  const lastVisible = firstVisible + visible.length - 1;

  return (
    <div className="space-y-6">
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
          <PageSpread pages={visible} />

          <nav className="flex items-center justify-between gap-4">
            <button
              type="button"
              onClick={() => setSpread(current - 1)}
              disabled={current === 0}
              className="rounded-xl border border-border bg-surface px-4 py-2 font-semibold disabled:opacity-40"
            >
              ← Previous
            </button>
            <span className="text-sm text-muted">
              {firstVisible === lastVisible
                ? `Page ${firstVisible}`
                : `Pages ${firstVisible}–${lastVisible}`}{" "}
              of {pages.length}
            </span>
            <button
              type="button"
              onClick={() => setSpread(current + 1)}
              disabled={current >= spreadCount - 1}
              className="rounded-xl border border-border bg-surface px-4 py-2 font-semibold disabled:opacity-40"
            >
              Next →
            </button>
          </nav>
        </>
      )}
    </div>
  );
}
