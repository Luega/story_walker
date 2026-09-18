import Link from "next/link";

// Public entry point. A server component with no data fetching: nothing here depends on who is
// looking, so it renders the same for a visitor and for a signed-in user (the header's UserButton
// is what tells them apart).
const STEPS = [
  {
    title: "Share your idea",
    body: "Chat with the agent: who the hero is, where the story takes place, which child it's for.",
  },
  {
    title: "The agent writes the plan",
    body: "It turns the conversation into a complete brief — title, age, illustration style, six pages.",
  },
  {
    title: "Get your story",
    body: "Text and illustrations are generated together, with characters that stay consistent from page to page.",
  },
];

export default function LandingPage() {
  return (
    <div className="space-y-16 py-8">
      <section className="max-w-2xl space-y-6">
        <h1 className="font-display text-5xl font-semibold tracking-tight sm:text-6xl">
          Illustrated stories, written for your child.
        </h1>
        <p className="text-lg text-muted">
          Describe the idea in your own words. Story Walker turns it into a six-page illustrated
          story, with the same characters from beginning to end.
        </p>
        <Link
          href="/sign-up"
          className="inline-block rounded-xl bg-accent px-6 py-3 font-semibold text-accent-foreground shadow-sm transition hover:brightness-105"
        >
          Get started
        </Link>
      </section>

      <section className="space-y-6">
        <h2 className="font-display text-2xl font-semibold tracking-tight">How it works</h2>
        <ol className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          {STEPS.map((step, i) => (
            <li
              key={step.title}
              className="rounded-2xl border border-border bg-surface px-6 py-6 space-y-2"
            >
              <span className="font-display text-3xl font-semibold text-accent">{i + 1}</span>
              <h3 className="font-semibold">{step.title}</h3>
              <p className="text-sm text-muted">{step.body}</p>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
