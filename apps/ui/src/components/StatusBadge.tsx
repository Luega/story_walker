import type { StoryStatus } from "@/lib/api";

const LABELS: Record<StoryStatus, { text: string; className: string }> = {
  Draft: { text: "Draft", className: "bg-lilac-soft text-foreground" },
  Generating: { text: "Generating", className: "bg-sun-soft text-foreground animate-pulse" },
  Complete: { text: "Ready", className: "bg-mint-soft text-foreground" },
  Failed: { text: "Failed", className: "bg-danger-soft text-danger" },
};

export function StatusBadge({ status }: { status: StoryStatus }) {
  const { text, className } = LABELS[status] ?? LABELS.Draft;
  return (
    <span className={`chip ${className}`}>
      {text}
    </span>
  );
}
