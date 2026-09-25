"use client";

import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import {
  deleteConversation,
  listConversations,
  renameConversation,
  type ConversationSummary,
} from "@/lib/api";
import { ConfirmDeleteDialog } from "./ConfirmDeleteDialog";

const dateFormat = new Intl.DateTimeFormat("en-GB", { dateStyle: "medium" });

/** Where a conversation stands, in the words the history uses. */
function stateOf(c: ConversationSummary): { text: string; className: string } {
  if (c.storyFailed) {
    return { text: "Story failed · continue to retry", className: "bg-red-100 text-red-800" };
  }
  if (c.resumable) return { text: "In progress", className: "bg-amber-100 text-amber-800" };
  return { text: "Story created", className: "bg-emerald-100 text-emerald-800" };
}

/**
 * The conversation history (ADR-0028): every conversation, most recent activity first, in a panel
 * that slides in from the left. Choosing one hands its id to the dashboard, which opens it in the
 * chat modal. Renaming and deleting happen here.
 *
 * Mounted only while open, so the list is fetched fresh each time: a chat that just closed may have
 * added a conversation or changed where one stands. A <dialog>, like the chat and the delete
 * confirmation, for the focus trap, Esc and backdrop.
 */
export function ConversationSidebar({
  onOpen,
  onClose,
}: {
  onOpen: (id: string) => void;
  onClose: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [conversations, setConversations] = useState<ConversationSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<ConversationSummary | null>(null);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    dialogRef.current?.showModal();
  }, []);

  useEffect(() => {
    let cancelled = false;
    listConversations()
      .then((data) => !cancelled && setConversations(data))
      .catch((e: Error) => !cancelled && setError(e.message));
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleRename(conversation: ConversationSummary, title: string) {
    try {
      const saved = await renameConversation(conversation.id, title);
      setConversations(
        (list) => list?.map((c) => (c.id === conversation.id ? { ...c, title: saved } : c)) ?? null,
      );
      setError(null);
    } catch (e) {
      setError((e as Error).message);
      // Rethrow so the row keeps the typed title in edit mode instead of discarding it.
      throw e;
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return;
    setDeleting(true);
    try {
      await deleteConversation(pendingDelete.id);
      setConversations((list) => list?.filter((c) => c.id !== pendingDelete.id) ?? null);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setPendingDelete(null);
      setDeleting(false);
    }
  }

  return (
    <dialog
      ref={dialogRef}
      onClose={onClose}
      aria-labelledby="conversations-heading"
      className="fixed inset-y-0 left-0 m-0 flex h-dvh max-h-none w-[min(24rem,100vw)] flex-col border-r border-border bg-surface p-0 text-foreground shadow-2xl backdrop:bg-black/40 backdrop:backdrop-blur-sm"
    >
      <div className="flex items-center justify-between border-b border-border px-5 py-4">
        <h2 id="conversations-heading" className="font-display text-xl font-semibold">
          Conversations
        </h2>
        <button
          type="button"
          onClick={() => dialogRef.current?.close()}
          className="rounded-full px-2 text-2xl leading-none text-muted hover:text-foreground"
          aria-label="Close conversations"
        >
          ×
        </button>
      </div>

      <div className="flex-1 overflow-y-auto px-3 py-3">
        {error && (
          <p role="alert" className="mx-2 mb-3 rounded-lg bg-red-100 px-3 py-2 text-sm text-red-800">
            {error}
          </p>
        )}

        {conversations === null && !error && <p className="px-2 text-muted">Loading…</p>}

        {conversations?.length === 0 && (
          <p className="px-2 text-muted">No conversations yet. They appear here once you chat.</p>
        )}

        {conversations && conversations.length > 0 && (
          <ul className="space-y-1">
            {conversations.map((c) => (
              <ConversationRow
                key={c.id}
                conversation={c}
                onOpen={() => {
                  dialogRef.current?.close();
                  onOpen(c.id);
                }}
                onRename={handleRename}
                onDelete={setPendingDelete}
              />
            ))}
          </ul>
        )}
      </div>

      {pendingDelete && (
        <ConfirmDeleteDialog
          title={pendingDelete.title}
          heading="Delete this conversation?"
          description={`“${pendingDelete.title}” will be deleted. Stories created from it are kept. This cannot be undone.`}
          busy={deleting}
          onConfirm={confirmDelete}
          onCancel={() => !deleting && setPendingDelete(null)}
        />
      )}
    </dialog>
  );
}

/**
 * One conversation. The title is the way in; while renaming, an input replaces it, and Enter or
 * leaving the field saves while Escape discards, as on a story card.
 */
function ConversationRow({
  conversation,
  onOpen,
  onRename,
  onDelete,
}: {
  conversation: ConversationSummary;
  onOpen: () => void;
  onRename: (conversation: ConversationSummary, title: string) => Promise<void>;
  onDelete: (conversation: ConversationSummary) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(conversation.title);
  const [saving, setSaving] = useState(false);
  // Disabling the input steals its focus, which fires onBlur -> save() a second time. A ref (not
  // the `saving` state) because that re-entrant call happens before the next render.
  const savingRef = useRef(false);
  const state = stateOf(conversation);

  async function save() {
    if (savingRef.current) return;

    const title = draft.trim();
    if (!title || title === conversation.title) {
      setEditing(false);
      return;
    }

    savingRef.current = true;
    setSaving(true);
    try {
      await onRename(conversation, title);
      setEditing(false);
    } catch {
      // Stay in edit mode so the typed title is not lost; the sidebar shows the error.
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  }

  function onKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter") {
      e.preventDefault();
      void save();
    }
    if (e.key === "Escape") {
      // Escape would otherwise also close the whole sidebar.
      e.preventDefault();
      e.stopPropagation();
      setEditing(false);
    }
  }

  return (
    <li className="group rounded-xl px-2 py-2 hover:bg-background">
      <div className="flex items-start gap-1">
        <div className="min-w-0 flex-1">
          {editing ? (
            <input
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={onKeyDown}
              onBlur={() => void save()}
              disabled={saving}
              autoFocus
              aria-label="Conversation title"
              className="w-full rounded-lg border border-accent bg-background px-2 py-1 font-semibold outline-none disabled:opacity-60"
            />
          ) : (
            <button
              type="button"
              onClick={onOpen}
              className="block w-full truncate text-left font-semibold hover:text-accent"
            >
              {conversation.title}
            </button>
          )}
          <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted">
            <time dateTime={conversation.lastActivityAt}>
              {dateFormat.format(new Date(conversation.lastActivityAt))}
            </time>
            <span className={`rounded-full px-2 py-0.5 font-semibold ${state.className}`}>
              {state.text}
            </span>
          </div>
        </div>

        {!editing && (
          <div className="flex shrink-0 gap-1">
            <button
              type="button"
              onClick={() => {
                setDraft(conversation.title);
                setEditing(true);
              }}
              aria-label={`Rename “${conversation.title}”`}
              title="Rename"
              className="rounded-full p-1.5 text-muted transition hover:bg-surface hover:text-accent"
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
                className="h-4 w-4"
                aria-hidden
              >
                <path d="M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" />
              </svg>
            </button>
            <button
              type="button"
              onClick={() => onDelete(conversation)}
              aria-label={`Delete “${conversation.title}”`}
              title="Delete"
              className="rounded-full p-1.5 text-muted transition hover:bg-red-100 hover:text-red-700"
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                className="h-4 w-4"
                aria-hidden
              >
                <path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" />
              </svg>
            </button>
          </div>
        )}
      </div>
    </li>
  );
}
