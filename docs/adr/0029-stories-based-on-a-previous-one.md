# ADR 0029 — A new story based on a previous one, from a fresh conversation

- **Status:** Accepted
- **Date:** 2026-09-25
- **Deciders:** Luca Martinelli

## Context

Completed conversations are kept so that related stories (sequels, prequels) can build on them
([ADR-0028](0028-conversation-history.md)). But the history shows them read-only, and there is
no way to start a related story. They also share one list with the conversations still in
progress.

[ADR-0019](0019-mvp-scope-reduction.md) removed `Story.BasedOnStoryId` from the MVP, along with the
whole reuse model around it (a character table, reference images, embeddings).

A completed conversation holds the interview, but not the story that came out of it: the text and
the illustration prompts, which carry each character's full description, live on the story's pages.

## Decision

1. **Two tabs in the history sidebar:** *In progress*, for conversations that can be continued
   (including one whose story failed), and *Completed*.
2. **A related story starts in a new conversation.** A completed conversation opens read-only
   with a **"New story based on this one"** action, which starts a fresh chat seeded with the
   previous story. The new conversation's system message is the usual one plus a delimited
   **previous-story block**:
   - the previous story's title, age, art style, and each page's text and illustration prompt;
   - marked as data, not instructions, like the saved context of ADR-0022.

   The agent asks whether the new story is a sequel, a prequel or something else. It reuses the
   characters' descriptions word for word and the same art style unless the parent asks
   otherwise. The new story stays self-contained, with six pages.
3. **The brief author sees the block too.** The authoring pass drops system messages
   ([ADR-0013](0013-agent-authored-generation-brief.md)), so it adds the block back as its own
   system message. Continuity of characters and style depends on it.
4. **`BasedOnStoryId` returns, with `SET NULL`.**
   - `Conversation.BasedOnStoryId` records what a conversation was started from.
   - `Story.BasedOnStoryId` receives it when the story is queued.
   - Deleting the original leaves its sequels in place, only unlinked. Deletes stay independent,
     as ADR-0028 decided; the old `Restrict` of ADR-0017 is not brought back.
5. **Only a `Complete` story of the caller's can be a starting point.** Anything else is a `404`.
   A conversation offers the one story it produced most recently that is `Complete`.

## Consequences

### Positive
- A parent can go from a finished story to its sequel in two clicks. The new story has the same
  characters, described the same way, and the same look.
- Each conversation still belongs to one story line, so titles and states in the history stay
  meaningful.

### Negative / trade-offs accepted
- The previous story (six pages of text and prompts) is resent to the model on every turn of the
  new conversation. That is a few thousand tokens.
- If the original story is deleted mid-conversation, the author loses the block and continuity
  rests on the interview alone.
- The reuse model of ADR-0009 (shared character rows, reference images) is still not built. This
  reuses characters by copying their descriptions.

### Things this decision obliges us to build or watch
- A "series" view built on `BasedOnStoryId`, if wanted, is not part of this.
- The story card offers no "sequel" action yet; the history is the only entry point.

## Alternatives considered

### Continue the completed conversation itself
The agent would already have the interview, but not the story text. The conversation's title
would jump to the new story, and one thread would mix several stories.

### Start a new conversation without recording the link
Simpler, but the thread between stories would be lost for good.
