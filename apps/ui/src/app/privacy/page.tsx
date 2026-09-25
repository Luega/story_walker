import Link from "next/link";

/**
 * What Story Walker keeps, why, who can see it and how to delete it (ADR-0028). A DRAFT written by
 * the team, not by a lawyer: it has to be reviewed by someone qualified before launch, and kept in
 * step with what the code actually stores.
 */
export default function PrivacyPage() {
  return (
    <article className="mx-auto max-w-2xl space-y-6 leading-relaxed">
      <header>
        <h1 className="font-display text-4xl font-semibold tracking-tight">Privacy</h1>
        <p className="mt-2 rounded-lg bg-amber-100 px-3 py-2 text-sm text-amber-900">
          Draft: this page is still being reviewed.
        </p>
      </header>

      <section className="space-y-2">
        <h2 className="font-display text-2xl font-semibold">What we keep</h2>
        <ul className="list-disc space-y-1 pl-6">
          <li>Your account: the email address you sign in with (through Clerk).</li>
          <li>
            Your <Link href="/settings" className="underline">story settings</Link>: what you tell
            us about the children and the stories you like.
          </li>
          <li>
            Your conversations with the story agent: everything you and the agent write, including
            names, ages and descriptions of children.
          </li>
          <li>Your stories: their text and illustrations.</li>
          <li>
            A count of the stories you finished and when, to apply the weekly limit. It stays even
            if you delete a story.
          </li>
        </ul>
      </section>

      <section className="space-y-2">
        <h2 className="font-display text-2xl font-semibold">Why</h2>
        <p>
          To write and illustrate your stories. Conversations are kept so you can pick them up
          again, for example after a story could not be finished, and so that related stories, such
          as a sequel, can build on them in future.
        </p>
      </section>

      <section className="space-y-2">
        <h2 className="font-display text-2xl font-semibold">Who can see it</h2>
        <p>
          You. Our technical staff can also see what is stored when they need to, for example to
          fix a problem you reported. They do so only for that purpose.
        </p>
        <p>
          To write and illustrate the stories, your conversations and settings are sent to AI
          models hosted on Microsoft Azure (Azure AI Foundry). Everything is stored in Microsoft
          Azure: the database and the illustrations. Sign-in is handled by Clerk.
        </p>
      </section>

      <section className="space-y-2">
        <h2 className="font-display text-2xl font-semibold">How long, and how to delete it</h2>
        <p>
          We keep everything until you delete it. You can delete a conversation from the
          Conversations panel on your dashboard, and a story from its card. Deleting a conversation
          keeps the stories made from it; deleting a story keeps its conversation.
        </p>
      </section>
    </article>
  );
}
