import Link from "next/link";

/**
 * PLACEHOLDER: every picture on this page is a random photograph from Lorem Picsum, keyed by a
 * fixed seed so a slot always gets the same one. None of them shows Story Walker, and the portraits
 * beside the quotes are strangers, not the parents named there. Replace the lot - words and faces
 * together - before this page is shown to anyone.
 *
 * A plain <img>, as in PageSpread: next/image would mean naming picsum.photos in next.config.ts,
 * which puts a placeholder host into production config that has to be remembered and removed.
 */
function Placeholder({
  seed,
  width,
  height,
  alt,
  className = "",
  priority = false,
}: {
  seed: string;
  width: number;
  height: number;
  alt: string;
  className?: string;
  priority?: boolean;
}) {
  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={`https://picsum.photos/seed/${seed}/${width}/${height}`}
      alt={alt}
      width={width}
      height={height}
      loading={priority ? "eager" : "lazy"}
      className={`bg-surface object-cover ${className}`}
    />
  );
}

// Public entry point. A server component with no data fetching: nothing here depends on who is
// looking, so it renders the same for a visitor and for a signed-in user (the header's UserButton
// is what tells them apart).
const STEPS = [
  {
    title: "Say what tonight is about",
    body: "Tell the agent who your child is, and what the story should land: a feeling, a worry, a lesson that didn’t go down well over dinner.",
  },
  {
    title: "The agent writes the plan",
    body: "It asks for what it still needs, then turns the conversation into a brief — title, age, illustration style, six pages.",
  },
  {
    title: "Read it together",
    body: "Words and illustrations arrive together, the same characters from beginning to end, pitched at your child’s age.",
  },
];

// PLACEHOLDER: these parents are invented, and the app has had no users yet. Replace them with
// real, permitted quotes before this page is shown to anyone.
const REVIEWS = [
  {
    quote:
      "She asked for the one about the loud classroom three nights running. It’s the first time a book has been about her week.",
    name: "Marta",
    about: "mum of Nina, 5",
  },
  {
    quote:
      "I’d been avoiding the conversation about his grandad for a month. We read it instead, and then we had the conversation.",
    name: "Tom",
    about: "dad of Elia, 7",
  },
  {
    quote:
      "Made one about sharing the tablet while the pasta was cooking. Six pages, his name in it, done before dinner.",
    name: "Priya",
    about: "mum of Arun, 4",
  },
];

function StartStory({ className = "" }: { className?: string }) {
  return (
    <Link
      href="/sign-up"
      className={`inline-block rounded-xl bg-accent px-6 py-3 font-semibold text-accent-foreground shadow-sm transition hover:brightness-105 ${className}`}
    >
      Start a story
    </Link>
  );
}

export default function LandingPage() {
  return (
    <div className="space-y-20 py-8 sm:space-y-24">
      <section
        aria-labelledby="hero"
        className="grid grid-cols-1 items-center gap-10 md:grid-cols-2 md:gap-12"
      >
        <div className="space-y-6">
          <h1 id="hero" className="font-display text-5xl font-semibold tracking-tight sm:text-6xl">
            Tonight’s story doesn’t exist yet.
          </h1>
          <p className="text-lg text-muted">
            Tell Story Walker what your child is wrestling with — sharing, a new baby brother, the
            dark — and get back a six-page illustrated story about exactly that, with the same
            characters from the first page to the last.
          </p>
          <StartStory />
        </div>
        <Placeholder
          seed="bedtime"
          width={800}
          height={800}
          priority
          alt="Placeholder photograph standing in for a story being read at bedtime"
          className="aspect-square w-full rounded-2xl border border-border"
        />
      </section>

      {/* The origin story is set like a page of a book: the product's own voice, in its own type. */}
      <section aria-labelledby="origin" className="space-y-6">
        <h2 id="origin" className="font-display text-2xl font-semibold tracking-tight">
          Where it came from
        </h2>
        <div className="grid grid-cols-1 gap-8 md:grid-cols-[minmax(0,1fr)_minmax(0,1.6fr)] md:items-stretch">
          <Placeholder
            seed="shelf"
            width={700}
            height={900}
            alt="Placeholder photograph standing in for a shelf of children’s books"
            className="h-full max-h-[28rem] w-full rounded-2xl border border-border"
          />
          {/* Sized to the measure of the text inside it, so the panel is a page rather than a banner. */}
          <div className="rounded-2xl border border-border bg-surface px-6 py-8 sm:px-10 sm:py-12">
            <div className="max-w-[58ch] space-y-5 font-display text-lg leading-[1.75] sm:text-xl">
              <p>
                It’s a quarter past eight. Something happened at school today — a friend left out of
                a game, a toy that never got shared — and there are about four minutes left in which
                to say something about it that will still be there in the morning.
              </p>
              <p>
                So you go to the shelf. Thirty books. One is about a tractor. None is about this.
              </p>
              <p>
                Every parent has stood in front of that shelf. Story Walker was born there — not to
                replace the books your child already loves, but to make the one that isn’t on the
                shelf: the story about this week, this worry, this small thing you want to land
                gently, while they’re still listening.
              </p>
            </div>
          </div>
        </div>
      </section>

      <section aria-labelledby="how" className="space-y-8">
        <h2 id="how" className="font-display text-2xl font-semibold tracking-tight">
          How it works
        </h2>
        <ol className="grid grid-cols-1 gap-8 sm:grid-cols-3 sm:gap-6">
          {STEPS.map((step, i) => (
            <li key={step.title} className="space-y-2 border-t border-border pt-4">
              <span className="block font-display text-4xl font-semibold text-accent">{i + 1}</span>
              <h3 className="font-semibold">{step.title}</h3>
              <p className="text-sm leading-relaxed text-muted">{step.body}</p>
            </li>
          ))}
        </ol>
        {/* Two facing pages, the way the reader shows them. */}
        <div className="grid grid-cols-2 gap-1 overflow-hidden rounded-2xl border border-border">
          <Placeholder
            seed="page-left"
            width={800}
            height={800}
            alt="Placeholder photograph standing in for the left page of a finished story"
            className="aspect-square w-full"
          />
          <Placeholder
            seed="page-right"
            width={800}
            height={800}
            alt="Placeholder photograph standing in for the right page of a finished story"
            className="aspect-square w-full"
          />
        </div>
      </section>

      <section aria-labelledby="why" className="space-y-8">
        <h2 id="why" className="font-display text-2xl font-semibold tracking-tight">
          Why Story Walker exists
        </h2>
        <div className="grid grid-cols-1 gap-8 sm:grid-cols-2 sm:gap-10">
          <div className="space-y-3 border-t border-border pt-4">
            <h3 className="font-display text-xl font-semibold">The goal</h3>
            <p className="leading-relaxed text-muted">
              Going from “I need a story about this” to reading it aloud should take about as long
              as brushing teeth. Six pages, illustrated, written for the age of the child in front
              of you. Not a novel, and not a lecture with a dragon in it.
            </p>
          </div>
          <div className="space-y-3 border-t border-border pt-4">
            <h3 className="font-display text-xl font-semibold">The vision</h3>
            <p className="leading-relaxed text-muted">
              Children remember stories long after they stop listening to advice. If the right story
              can be made on the evening it’s needed, every family ends up with a shelf of its own:
              the book about the new baby, the book about the first day of school, the book about
              saying goodbye.
            </p>
          </div>
        </div>
      </section>

      <section aria-labelledby="reviews" className="space-y-8">
        <h2 id="reviews" className="font-display text-2xl font-semibold tracking-tight">
          What parents say
        </h2>
        <ul className="grid grid-cols-1 gap-8 sm:grid-cols-3 sm:gap-6">
          {REVIEWS.map((review) => (
            <li key={review.name}>
              {/* The caption names the figure, so a quote is never announced without its parent. */}
              <figure
                aria-labelledby={`review-${review.name}`}
                className="space-y-4 border-t border-border pt-4"
              >
                <blockquote className="font-display text-lg leading-[1.6]">
                  “{review.quote}”
                </blockquote>
                {/* The portrait sits beside the caption rather than inside it: within it, its alt
                    text would become part of the name the quote is announced under. */}
                <div className="flex items-center gap-3">
                  <Placeholder
                    seed={`parent-${review.name}`}
                    width={96}
                    height={96}
                    alt={`Placeholder portrait standing in for ${review.name}`}
                    className="size-12 shrink-0 rounded-full border border-border"
                  />
                  <figcaption id={`review-${review.name}`} className="text-sm text-muted">
                    {review.name}, {review.about}
                  </figcaption>
                </div>
              </figure>
            </li>
          ))}
        </ul>
      </section>

      <section className="max-w-2xl space-y-6 border-t border-border pt-12">
        <h2 className="font-display text-3xl font-semibold tracking-tight sm:text-4xl">
          Your child’s next favourite book hasn’t been written yet.
        </h2>
        <StartStory />
      </section>
    </div>
  );
}
