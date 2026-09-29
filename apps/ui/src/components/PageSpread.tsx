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
    <div className="absolute inset-0 flex items-center justify-center bg-sky-soft p-6 text-center font-semibold text-muted">
      {page.status === "Failed" ? (
        <span>Illustration unavailable</span>
      ) : (
        <span className="animate-pulse">Illustration on its way…</span>
      )}
    </div>
  );
}

/**
 * One page of the book, as a card: the illustration on top, never covered, and the story below it
 * (ADR-0033). `folio` follows the convention of a printed book, where the page number sits in the
 * outer corner - away from the spine.
 *
 * In the full-screen reader the card has to fit the screen: the picture stays square but no taller
 * than about half of it, and a long text scrolls inside the card rather than pushing the card off.
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
      className={`card flex flex-col gap-4 p-3 ${
        immersive ? "max-h-full min-h-0 w-full max-w-2xl" : ""
      }`}
    >
      <div
        className={`relative mx-auto aspect-square w-full shrink-0 overflow-hidden rounded-[1.5rem] ${
          immersive ? "max-w-[55dvh]" : ""
        }`}
      >
        <Illustration page={page} />
      </div>
      <div className={`px-3 pb-2 sm:px-4 ${immersive ? "min-h-0 flex-1 overflow-y-auto" : ""}`}>
        <p className="mx-auto max-w-prose font-display text-xl leading-relaxed whitespace-pre-line sm:text-2xl">
          {page.content}
        </p>
        <span
          className={`mt-3 flex size-9 items-center justify-center rounded-full bg-sun-soft font-display text-sm font-semibold text-muted ${
            folio === "left" ? "mr-auto" : "ml-auto"
          }`}
        >
          {page.pageNo}
        </span>
      </div>
    </article>
  );
}

/**
 * The pages currently on show: one on a phone or a tablet, two side by side on a desktop.
 */
export function PageSpread({ pages, immersive }: { pages: Page[]; immersive: boolean }) {
  return (
    <div
      className={
        immersive
          ? "flex h-full min-h-0 w-full items-center justify-center"
          : "grid grid-cols-1 items-start gap-6 lg:grid-cols-2"
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
