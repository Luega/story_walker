import type { StoryStatus } from "@/lib/api";

const LABELS: Record<StoryStatus, { text: string; className: string }> = {
  Draft: { text: "Bozza", className: "bg-stone-200 text-stone-700" },
  Generating: { text: "In generazione", className: "bg-amber-100 text-amber-800 animate-pulse" },
  Complete: { text: "Pronta", className: "bg-emerald-100 text-emerald-800" },
  Failed: { text: "Errore", className: "bg-red-100 text-red-800" },
};

export function StatusBadge({ status }: { status: StoryStatus }) {
  const { text, className } = LABELS[status] ?? LABELS.Draft;
  return (
    <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold ${className}`}>
      {text}
    </span>
  );
}
