"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getStory, type Story } from "@/lib/api";
import { PageSpread } from "@/components/PageSpread";
import { StatusBadge } from "@/components/StatusBadge";

const POLL_MS = 3000;
const PAGES_PER_SPREAD = 2;

type LoadState =
  | { kind: "loading" }
  | { kind: "notFound" }
  | { kind: "error"; message: string }
  | { kind: "loaded"; story: Story };

function isInProgress(story: Story) {
  return story.status === "Generating" || story.pages.some((p) => p.status === "Pending");
}

export default function StoryPage() {
  const { id } = useParams<{ id: string }>();
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [spread, setSpread] = useState(0);

  useEffect(() => {
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    async function load() {
      try {
        const story = await getStory(id);
        if (cancelled) return;
        if (!story) {
          setState({ kind: "notFound" });
          return;
        }
        setState({ kind: "loaded", story });
        if (isInProgress(story)) timer = setTimeout(load, POLL_MS);
      } catch (e) {
        if (!cancelled) setState({ kind: "error", message: (e as Error).message });
      }
    }

    void load();
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [id]);

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

  if (state.kind === "loading") return <p className="text-muted">Caricamento…</p>;

  if (state.kind === "notFound" || state.kind === "error") {
    return (
      <div className="space-y-4 py-16 text-center">
        <p className="font-display text-2xl">
          {state.kind === "notFound" ? "Storia non trovata." : state.message}
        </p>
        <Link href="/" className="font-semibold text-accent hover:underline">
          ← Torna alle storie
        </Link>
      </div>
    );
  }

  const { story: s } = state;
  const firstVisible = current * PAGES_PER_SPREAD + 1;
  const lastVisible = firstVisible + visible.length - 1;

  return (
    <div className="space-y-6">
      <Link href="/" className="text-sm font-semibold text-muted hover:text-accent">
        ← Tutte le storie
      </Link>

      <div className="space-y-2">
        <h1 className="font-display text-4xl font-semibold tracking-tight">{s.title}</h1>
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted">
          <span>Età {s.targetAge}+</span>
          <span aria-hidden>·</span>
          <span>{s.artStyle}</span>
          <StatusBadge status={s.status} />
        </div>
      </div>

      {s.status === "Generating" && (
        <p className="rounded-lg bg-amber-100 px-4 py-3 text-amber-900" aria-live="polite">
          Generazione in corso… le pagine appariranno qui man mano che sono pronte.
        </p>
      )}
      {s.status === "Failed" && (
        <p role="alert" className="rounded-lg bg-red-100 px-4 py-3 text-red-800">
          La generazione di questa storia non è andata a buon fine.
        </p>
      )}

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
              ← Precedente
            </button>
            <span className="text-sm text-muted">
              {firstVisible === lastVisible
                ? `Pagina ${firstVisible}`
                : `Pagine ${firstVisible}–${lastVisible}`}{" "}
              di {pages.length}
            </span>
            <button
              type="button"
              onClick={() => setSpread(current + 1)}
              disabled={current >= spreadCount - 1}
              className="rounded-xl border border-border bg-surface px-4 py-2 font-semibold disabled:opacity-40"
            >
              Successiva →
            </button>
          </nav>
        </>
      )}
    </div>
  );
}
