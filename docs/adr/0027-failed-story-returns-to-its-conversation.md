# ADR 0027 — A failed story returns to its conversation, with the agent's advice

- **Status:** Accepted
- **Date:** 2026-09-25
- **Deciders:** Luca Martinelli

## Context

Since [ADR-0026](0026-one-failed-page-fails-the-story.md) a story fails as soon as one page cannot be
illustrated, and a failed story can only be deleted. The conversation that planned it stays
`Completed`, with no trace of what happened. The parent has lost the story and also the context
that produced it.

A conversation history is planned. From it, a parent should be able to reopen a failed story's
conversation at the point where they confirmed it, and find the agent explaining what went wrong
and what to change. Three constraints shape how:

- A `Story` has no link to the `Conversation` that produced it.
- The parent must never see the technical error: not the image provider's response body, not
  codes like `moderation_blocked`.
- The conversation already contains `__END__CHAT__`, the sentinel that starts generation, in the
  agent's "generating now" message. The interview prompt allows it "only in this final message for
  the entire conversation".

## Decision

1. **`Story.ConversationId`** (nullable, `SET NULL` on delete) links a story to the conversation
   whose confirmation queued it. The API sets it at enqueue. Stories from before this change have
   none, and nothing is recorded for them.
2. **`PageImageRenderer.RenderAsync` reports why a page failed:** `Refused` (a moderation refusal
   that survived the ADR-0025 rewrite) or `Failed` (anything else).
3. **When a story settles `Failed`, the worker appends two messages to its conversation:**
   - a **system note**, never shown to the parent. It gives the category of the failure, the page
     number and the page text, and never the error body or the prompt. It tells the agent that the
     earlier confirmation no longer counts, how to answer, and that a new confirmation ends with
     `__END__CHAT__` again;
   - the **agent's reply** to that note, from one chat call over the whole conversation. It is
     written in the parent's language, explains the problem simply and suggests a concrete change.
     This is what the parent will see. If the call fails, a fixed fallback sentence is used
     instead.

   The conversation then goes back to `Active`, so it can be continued.
4. **Handling the sentinel:**
   - The old sentinel stays: messages are append-only ([ADR-0018](0018-persist-agent-conversations.md)),
     and `StoryAgent` only looks for the sentinel in new replies.
   - The system note explicitly allows it once more.
   - The advice message has any sentinel stripped before it is saved, so it can never start
     generation by itself.
   - The brief-authoring prompt now says to author the **most recent** confirmed plan.
5. **Recording is idempotent.** It happens only while the conversation is still `Completed`, so a
   redelivered message does not append the messages twice.

## Consequences

### Positive
- A failed story keeps its context. When the history exists, the parent resumes where they
  confirmed, with advice in their own words instead of a dead end.
- The technical cause is kept, as a category, in the conversation, for whoever investigates.

### Negative / trade-offs accepted
- Every failed story costs one extra chat call, even if nobody ever reopens it.
- The fallback sentence is in English, whatever language the conversation used.
- A conversation can now carry more than one confirmation, so the authoring pass depends on a
  prompt rule to pick the latest.

### Things this decision obliges us to build or watch
- The conversation history must hide `System` messages and strip `__END__CHAT__` from what it
  shows.
- Reopening a conversation creates a **new** story. The failed one stays deletable as before.

## Alternatives considered

### Generate the advice when the conversation is reopened
It avoids a chat call for failures nobody revisits, but it puts logic into a feature that does not
exist yet, and makes the parent wait at the moment they come back.

### Show the error message itself
The provider's message is technical, often in English, and it says nothing about what to change.
