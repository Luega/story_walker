"use client";

import { useEffect, useRef } from "react";

/**
 * Confirmation before an irreversible delete. Mounted only while something is pending deletion.
 * <dialog> gives us the backdrop, focus trap and Esc-to-close for free (same as NewStoryModal).
 * The wording defaults to a story's; the conversation history passes its own (ADR-0028).
 */
export function ConfirmDeleteDialog({
  title,
  heading = "Delete this story?",
  description = `“${title}” will be deleted along with all its pages. This cannot be undone.`,
  busy,
  onConfirm,
  onCancel,
}: {
  title: string;
  heading?: string;
  description?: string;
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    dialogRef.current?.showModal();
  }, []);

  return (
    <dialog
      ref={dialogRef}
      onClose={onCancel}
      onCancel={(e) => busy && e.preventDefault()}
      className="m-auto w-[min(28rem,calc(100vw-2rem))] rounded-[2rem] border-2 border-border bg-surface p-7 text-foreground shadow-2xl backdrop:bg-foreground/30 backdrop:backdrop-blur-sm"
    >
      <h2 className="font-display text-xl font-semibold">{heading}</h2>
      <p className="mt-2 leading-relaxed text-muted">{description}</p>

      <div className="mt-6 flex justify-end gap-2">
        <button
          type="button"
          onClick={() => dialogRef.current?.close()}
          disabled={busy}
          autoFocus
          className="btn-secondary"
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={busy}
          className="btn-danger"
        >
          {busy ? "Deleting…" : "Delete"}
        </button>
      </div>
    </dialog>
  );
}
