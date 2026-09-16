"use client";

import { useEffect, useRef } from "react";

/**
 * Confirmation before an irreversible delete. Mounted only while a story is pending deletion.
 * <dialog> gives us the backdrop, focus trap and Esc-to-close for free (same as NewStoryModal).
 */
export function ConfirmDeleteDialog({
  title,
  busy,
  onConfirm,
  onCancel,
}: {
  title: string;
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
      className="m-auto w-[min(28rem,calc(100vw-2rem))] rounded-2xl border border-border bg-surface p-6 text-foreground shadow-2xl backdrop:bg-black/40 backdrop:backdrop-blur-sm"
    >
      <h2 className="font-display text-xl font-semibold">Eliminare la storia?</h2>
      <p className="mt-2 leading-relaxed text-muted">
        «{title}» verrà eliminata insieme a tutte le sue pagine. L&apos;azione è irreversibile.
      </p>

      <div className="mt-6 flex justify-end gap-2">
        <button
          type="button"
          onClick={() => dialogRef.current?.close()}
          disabled={busy}
          autoFocus
          className="rounded-xl border border-border px-4 py-2.5 font-semibold disabled:opacity-50"
        >
          Annulla
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={busy}
          className="rounded-xl bg-red-700 px-4 py-2.5 font-semibold text-white transition hover:brightness-110 disabled:opacity-50"
        >
          {busy ? "Eliminazione…" : "Elimina"}
        </button>
      </div>
    </dialog>
  );
}
