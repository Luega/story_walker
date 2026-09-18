"use client";

import { useEffect, useState } from "react";
import { deleteStory, listStories, renameStory, type StorySummary } from "@/lib/api";
import { StoryCard } from "@/components/StoryCard";
import { NewStoryModal } from "@/components/NewStoryModal";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";

const POLL_MS = 5000;

export default function DashboardPage() {
  const [stories, setStories] = useState<StorySummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<StorySummary | null>(null);
  const [deleting, setDeleting] = useState(false);

  const generating = stories?.some((s) => s.status === "Generating") ?? false;

  useEffect(() => {
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    async function load() {
      try {
        const data = await listStories();
        if (cancelled) return;
        setStories(data);
        setError(null);
        // Keep refreshing only while something is still being generated.
        if (data.some((s) => s.status === "Generating")) timer = setTimeout(load, POLL_MS);
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      }
    }

    void load();
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [generating]);

  async function handleRename(story: StorySummary, title: string) {
    try {
      const saved = await renameStory(story.id, title);
      setStories(
        (list) => list?.map((s) => (s.id === story.id ? { ...s, title: saved } : s)) ?? null,
      );
      setError(null);
    } catch (e) {
      setError((e as Error).message);
      // Rethrow so the card keeps the typed title in edit mode instead of discarding it.
      throw e;
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return;
    setDeleting(true);
    try {
      await deleteStory(pendingDelete.id);
      // Drop it locally instead of refetching: the list is already up to date otherwise.
      setStories((list) => list?.filter((s) => s.id !== pendingDelete.id) ?? null);
      setError(null);
      setPendingDelete(null);
    } catch (e) {
      setError((e as Error).message);
      setPendingDelete(null);
    } finally {
      setDeleting(false);
    }
  }

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="font-display text-4xl font-semibold tracking-tight">Le tue storie</h1>
          <p className="mt-1 text-muted">Scegli una storia da leggere o creane una nuova.</p>
        </div>
        <button
          type="button"
          onClick={() => setModalOpen(true)}
          className="rounded-xl bg-accent px-5 py-3 font-semibold text-accent-foreground shadow-sm transition hover:brightness-105"
        >
          + Nuova storia
        </button>
      </div>

      {error && (
        <p role="alert" className="rounded-lg bg-red-100 px-4 py-3 text-red-800">
          {error}
        </p>
      )}

      {stories === null && !error && <p className="text-muted">Caricamento…</p>}

      {stories?.length === 0 && (
        <div className="rounded-2xl border border-dashed border-border px-6 py-16 text-center text-muted">
          Nessuna storia ancora. Premi <strong>Nuova storia</strong> per iniziare.
        </div>
      )}

      {stories && stories.length > 0 && (
        <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {stories.map((s) => (
            <li key={s.id} className="flex">
              <StoryCard story={s} onDelete={setPendingDelete} onRename={handleRename} />
            </li>
          ))}
        </ul>
      )}

      {modalOpen && <NewStoryModal onClose={() => setModalOpen(false)} />}

      {pendingDelete && (
        <ConfirmDeleteDialog
          title={pendingDelete.title}
          busy={deleting}
          onConfirm={confirmDelete}
          onCancel={() => !deleting && setPendingDelete(null)}
        />
      )}
    </div>
  );
}
