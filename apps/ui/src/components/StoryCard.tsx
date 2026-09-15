import Link from "next/link";
import type { StorySummary } from "@/lib/api";
import { StatusBadge } from "./StatusBadge";

const dateFormat = new Intl.DateTimeFormat("it-IT", { dateStyle: "medium" });

export function StoryCard({ story }: { story: StorySummary }) {
  return (
    <Link
      href={`/stories/${story.id}`}
      className="group flex w-full flex-col gap-3 rounded-2xl border border-border bg-surface p-5 transition hover:-translate-y-0.5 hover:border-accent hover:shadow-md"
    >
      <h2 className="font-display text-xl font-semibold leading-snug group-hover:text-accent">
        {story.title}
      </h2>
      <div className="mt-auto flex items-center justify-between gap-2 text-sm text-muted">
        <time dateTime={story.createdAt}>{dateFormat.format(new Date(story.createdAt))}</time>
        <StatusBadge status={story.status} />
      </div>
    </Link>
  );
}
