import type { Page } from "@/lib/api";

function Illustration({ page }: { page: Page }) {
  if (page.imageUrl) {
    return (
      // Images come from Blob Storage/Azurite at arbitrary hosts; a plain <img> avoids
      // configuring next/image remote patterns for each environment.
      // eslint-disable-next-line @next/next/no-img-element
      <img
        src={page.imageUrl}
        alt={`Illustration for page ${page.pageNo}`}
        className="absolute inset-0 h-full w-full object-cover"
      />
    );
  }

  return (
    <div className="absolute inset-0 flex items-center justify-center bg-background p-6 text-center text-muted">
      {page.status === "Failed" ? (
        <span>Illustration unavailable</span>
      ) : (
        <span className="animate-pulse">Illustration on its way…</span>
      )}
    </div>
  );
}

/**
 * One page of the book: the illustration fills it, and the story sits over the artwork on a pane of
 * frosted vellum. `folio` follows the convention of a printed book, where the page number sits in
 * the outer corner - away from the spine.
 */
function StoryPage({
  page,
  folio,
  immersive,
}: {
  page: Page;
  folio: "left" | "right";
  immersive: boolean;
}) {
  return (
    <article
      className={
        immersive
          ? "relative h-full flex-1 overflow-hidden"
          : "relative aspect-square overflow-hidden rounded-2xl border border-border bg-background shadow-sm"
      }
    >
      <Illustration page={page} />
      <div
        className={`absolute inset-x-0 bottom-0 max-h-[60%] overflow-y-auto border-t border-glass-edge bg-glass px-5 pt-5 backdrop-blur-xl backdrop-saturate-150 ${
          immersive
            ? "pb-[calc(env(safe-area-inset-bottom)+4.5rem)] sm:px-7"
            : "pb-5 sm:px-7"
        }`}
      >
        <p className="mx-auto max-w-prose whitespace-pre-line font-display text-lg leading-[1.7] sm:text-xl">
          {page.content}
        </p>
        <span
          className={`mt-4 block font-display text-sm text-muted ${
            folio === "left" ? "text-left" : "text-right"
          }`}
        >
          {page.pageNo}
        </span>
      </div>
    </article>
  );
}

/**
 * The pages currently on show: one on a phone, two facing each other from tablet width up, parted
 * by a hairline where the spine of the book would be.
 */
export function PageSpread({ pages, immersive }: { pages: Page[]; immersive: boolean }) {
  return (
    <div
      className={
        immersive
          ? "flex h-full w-full divide-x divide-border/70"
          : "grid grid-cols-1 gap-6 md:grid-cols-2"
      }
    >
      {pages.map((p, i) => (
        <StoryPage
          key={p.pageNo}
          page={p}
          folio={i === 0 && pages.length > 1 ? "left" : "right"}
          immersive={immersive}
        />
      ))}
    </div>
  );
}
