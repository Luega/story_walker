import type { Page } from "@/lib/api";

function StoryPage({ page }: { page: Page }) {
  return (
    <article className="flex flex-col overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
      <div className="relative aspect-square w-full bg-background">
        {page.imageUrl ? (
          // Images come from Blob Storage/Azurite at arbitrary hosts; a plain <img> avoids
          // configuring next/image remote patterns for each environment.
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={page.imageUrl}
            alt={`Illustration for page ${page.pageNo}`}
            className="h-full w-full object-cover"
          />
        ) : page.status === "Failed" ? (
          <div className="flex h-full items-center justify-center p-6 text-center text-muted">
            Illustration unavailable
          </div>
        ) : (
          <div className="flex h-full animate-pulse items-center justify-center text-muted">
            Illustration on its way…
          </div>
        )}
      </div>
      <div className="flex flex-1 flex-col gap-4 p-6">
        <p className="whitespace-pre-line text-lg leading-relaxed">{page.content}</p>
        <span className="mt-auto self-end font-display text-sm text-muted">{page.pageNo}</span>
      </div>
    </article>
  );
}

/** Two facing pages, side by side on wide screens and stacked on narrow ones. */
export function PageSpread({ pages }: { pages: Page[] }) {
  return (
    <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
      {pages.map((p) => (
        <StoryPage key={p.pageNo} page={p} />
      ))}
    </div>
  );
}
