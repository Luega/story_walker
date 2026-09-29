"use client";

import { useRef, useState, type KeyboardEvent } from "react";
import Link from "next/link";
import type { StorySummary } from "@/lib/api";
import { StatusBadge } from "./StatusBadge";

const dateFormat = new Intl.DateTimeFormat("en-GB", { dateStyle: "medium" });

// Each card wears one of the playful colours along its top, so a shelf of stories looks like a
// shelf of different books. Picked from the id, so a story keeps its colour between visits.
const COVERS = ["bg-sun", "bg-sky", "bg-mint", "bg-lilac", "bg-peach"];

function coverOf(id: string): string {
  let hash = 0;
  for (const c of id) hash = (hash * 31 + c.charCodeAt(0)) >>> 0;
  return COVERS[hash % COVERS.length];
}

/**
 * The link covers the whole card through an ::after overlay rather than wrapping it, so the action
 * buttons can sit inside the card without nesting a button in an anchor. While renaming, the input
 * replaces the link entirely — that removes the overlay instead of fighting it for the clicks.
 * Both actions only report intent; the dashboard owns the API calls and the list.
 *
 * Only a Complete story links anywhere: the others have nothing to read yet, so the title is plain
 * text and the card drops the affordances that would suggest otherwise. A story still generating
 * can be renamed and deleted. A failed one can only be deleted (ADR-0026): a light red tint
 * marks it inactive, the rename action is gone, and delete, its only action, is always visible.
 */
export function StoryCard({
  story,
  onDelete,
  onRename,
}: {
  story: StorySummary;
  onDelete: (story: StorySummary) => void;
  onRename: (story: StorySummary, title: string) => Promise<void>;
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(story.title);
  const [saving, setSaving] = useState(false);
  // Disabling the input steals its focus, which fires onBlur -> save() a second time. A ref (not
  // the `saving` state) because that re-entrant call happens before the next render.
  const savingRef = useRef(false);

  function startEditing() {
    setDraft(story.title);
    setEditing(true);
  }

  async function save() {
    if (savingRef.current) return;

    const title = draft.trim();
    // Nothing to persist: leave edit mode without bothering the server.
    if (!title || title === story.title) {
      setEditing(false);
      return;
    }

    savingRef.current = true;
    setSaving(true);
    try {
      await onRename(story, title);
      setEditing(false);
    } catch {
      // Stay in edit mode so the typed title is not lost; the dashboard shows the error.
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
      e.preventDefault();
      setEditing(false);
    }
  }

  const readable = story.status === "Complete";
  const failed = story.status === "Failed";
  const cardClassName = [
    "card group relative flex w-full flex-col gap-3 overflow-hidden p-5 pt-8 transition focus-within:border-accent",
    readable ? "hover:-translate-y-1 hover:rotate-[-0.5deg] hover:border-accent" : "",
  ].join(" ");
  // Dimmed under the overlay, so a failed card reads as inactive and not just as tinted.
  const inactive = failed ? "opacity-60 grayscale" : "";
  // Hover-revealed on wide screens, except when delete is the only thing the card still offers.
  const actionVisibility = failed
    ? "opacity-100"
    : "opacity-100 focus-visible:opacity-100 sm:opacity-0 sm:group-hover:opacity-100";

  return (
    <div className={cardClassName}>
      <div aria-hidden className={`absolute inset-x-0 top-0 h-3 ${coverOf(story.id)} ${inactive}`} />
      {failed && (
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 rounded-[2rem] bg-danger-soft/60"
        />
      )}

      <h2
        className={`pr-16 font-display text-xl font-semibold leading-snug ${
          readable ? "group-hover:text-accent" : "text-muted"
        } ${inactive}`}
      >
        {editing ? (
          <input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={onKeyDown}
            onBlur={() => void save()}
            disabled={saving}
            autoFocus
            aria-label="Story title"
            className="field px-3 py-1 font-display text-xl font-semibold"
          />
        ) : readable ? (
          <Link
            href={`/stories/${story.id}`}
            className="outline-none after:absolute after:inset-0 after:rounded-[2rem]"
          >
            {story.title}
          </Link>
        ) : (
          <span className="cursor-not-allowed">{story.title}</span>
        )}
      </h2>

      {!editing && (
        <div className="absolute top-5 right-3 z-10 flex gap-1">
          {!failed && (
            <button
              type="button"
              onClick={startEditing}
              aria-label={`Rename “${story.title}”`}
              title="Rename"
              className={`rounded-full p-2 text-muted transition hover:bg-lilac-soft hover:text-accent ${actionVisibility}`}
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
                className="h-5 w-5"
                aria-hidden
              >
                <path d="M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" />
              </svg>
            </button>
          )}

          <button
            type="button"
            onClick={() => onDelete(story)}
            aria-label={`Delete “${story.title}”`}
            title="Delete"
            className={`rounded-full p-2 text-muted transition hover:bg-danger-soft hover:text-danger ${actionVisibility}`}
          >
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.8"
              strokeLinecap="round"
              className="h-5 w-5"
              aria-hidden
            >
              <path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" />
            </svg>
          </button>
        </div>
      )}

      <div className={`mt-auto flex items-center justify-between gap-2 text-sm text-muted ${inactive}`}>
        <time dateTime={story.createdAt}>{dateFormat.format(new Date(story.createdAt))}</time>
        <StatusBadge status={story.status} />
      </div>
    </div>
  );
}
