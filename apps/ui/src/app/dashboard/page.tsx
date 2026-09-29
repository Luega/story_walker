"use client";

import { useEffect, useState } from "react";
import {
  deleteStory,
  getQuota,
  listStories,
  renameStory,
  type Quota,
  type StorySummary,
} from "@/lib/api";
import { StoryCard } from "@/components/StoryCard";
import { NewStoryModal, type BasedOn } from "@/components/NewStoryModal";
import { ConversationSidebar } from "@/components/ConversationSidebar";
import { ConfirmDeleteDialog } from "@/components/ConfirmDeleteDialog";

const POLL_MS = 5000;

/** When the weekly limit resets, in the browser's own time zone (the API counts in UTC). */
const resetFormat = new Intl.DateTimeFormat("en-GB", {
  weekday: "long",
  day: "numeric",
  month: "long",
  hour: "2-digit",
  minute: "2-digit",
});

export default function DashboardPage() {
  const [stories, setStories] = useState<StorySummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  // The conversation the chat opens on; null starts a new one (ADR-0028).
  const [openConversationId, setOpenConversationId] = useState<string | null>(null);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  // The finished story a new one builds on (ADR-0029); null for a story from scratch.
  const [basedOn, setBasedOn] = useState<BasedOn | null>(null);
  // The weekly limit (ADR-0030); null until known, or if it could not be loaded.
  const [quota, setQuota] = useState<Quota | null>(null);
  const atLimit = quota?.remaining === 0;
  const [pendingDelete, setPendingDelete] = useState<StorySummary | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [queuedId, setQueuedId] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const generating = stories?.some((s) => s.status === "Generating") ?? false;

  const queued = queuedId === null ? undefined : stories?.find((s) => s.id === queuedId);
  // Announced from the moment the agent queues the story until it stops generating. `undefined`
  // covers the gap before the refetch lands; the dismiss button covers a story that never returns.
  const showQueuedNotice =
    queuedId !== null && (queued === undefined || queued.status === "Generating");

  useEffect(() => {
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    async function load() {
      // Refreshed with the list, since a story finishing is what moves it. A failure only hides
      // the counter: the API still enforces the limit.
      getQuota()
        .then((q) => !cancelled && setQuota(q))
        .catch(() => {});
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
  }, [generating, reloadKey]);

  /** The agent authored the brief: show the new story here rather than waiting for the next poll. */
  function handleQueued(storyId: string) {
    setQueuedId(storyId);
    setReloadKey((k) => k + 1);
  }

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
          <h1 className="font-display text-4xl font-semibold tracking-tight">Your stories</h1>
          <p className="mt-1 text-muted">Pick a story to read, or create a new one.</p>
          {quota && !atLimit && (
            <p className="mt-1 text-sm text-muted">
              {quota.remaining} of {quota.limit} stories left this week
            </p>
          )}
          {quota && atLimit && (
            <p className="mt-2 inline-block rounded-2xl bg-sun-soft px-4 py-2 text-sm font-semibold">
              You&apos;ve used this week&apos;s {quota.limit} stories. You can create more from{" "}
              {resetFormat.format(new Date(quota.resetsAt))}.
            </p>
          )}
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => setSidebarOpen(true)}
            className="btn-secondary px-6 py-3"
          >
            Conversations
          </button>
          <button
            type="button"
            onClick={() => {
              setOpenConversationId(null);
              setBasedOn(null);
              setModalOpen(true);
            }}
            disabled={atLimit}
            className="btn-primary px-6 py-3"
          >
            + New story
          </button>
        </div>
      </div>

      {showQueuedNotice && (
        <div
          role="status"
          className="flex items-center justify-between gap-4 rounded-3xl bg-mint-soft px-5 py-3 font-semibold text-foreground"
        >
          <p>Your story is being generated — it will appear below in a moment.</p>
          <button
            type="button"
            onClick={() => setQueuedId(null)}
            aria-label="Dismiss"
            className="btn-icon"
          >
            ×
          </button>
        </div>
      )}

      {error && (
        <p role="alert" className="rounded-3xl bg-danger-soft px-5 py-3 font-semibold text-danger">
          {error}
        </p>
      )}

      {stories === null && !error && <p className="text-muted">Loading…</p>}

      {stories?.length === 0 && (
        <div className="rounded-[2rem] border-2 border-dashed border-lilac bg-lilac-soft/50 px-6 py-16 text-center text-muted">
          No stories yet. Press <strong>New story</strong> to get started.
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

      {sidebarOpen && (
        <ConversationSidebar
          onClose={() => setSidebarOpen(false)}
          onOpen={(id) => {
            setSidebarOpen(false);
            setOpenConversationId(id);
            setBasedOn(null);
            setModalOpen(true);
          }}
        />
      )}

      {modalOpen && (
        <NewStoryModal
          // Remounted whenever what it opens on changes, so no state leaks between chats.
          key={openConversationId ?? (basedOn ? `based-on:${basedOn.storyId}` : "new")}
          conversationId={openConversationId ?? undefined}
          basedOn={basedOn ?? undefined}
          onClose={() => setModalOpen(false)}
          onQueued={handleQueued}
          onStartBasedOn={(chosen) => {
            setOpenConversationId(null);
            setBasedOn(chosen);
          }}
        />
      )}

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
