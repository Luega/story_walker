# ADR 0028 — Conversation history: kept, titled, resumable, and disclosed

- **Status:** Accepted
- **Date:** 2026-09-25
- **Deciders:** Luca Martinelli

## Context

Conversations with the agent are persisted ([ADR-0018](0018-persist-agent-conversations.md)), but
the only way to see one is the chat modal that created it. Once it closes, the conversation is
out of reach. A story that fails reopens its conversation with the agent's advice
([ADR-0027](0027-failed-story-returns-to-its-conversation.md)), and nothing in the UI leads back
to it.

The conversations are also personal data about children: names, ages, how they look. They stay in
our database, and our technical staff can see them.

## Decision

1. **A conversation history.** A sidebar on the dashboard lists every conversation of the
   signed-in user, most recent activity first. Clicking a title opens the chat modal on that
   conversation.
   - An `Active` one continues: a story not generated yet, or one that failed.
   - Any other one opens read-only.
2. **Conversations are kept after their story is created.** They are the starting point for
   related stories, such as sequels and prequels, which are planned but not built. They are
   deleted only when the user deletes them.
3. **Stories and conversations are deleted independently.** Deleting a story never touches its
   conversation. Deleting a conversation deletes its messages; its stories stay and lose the link
   (`Story.ConversationId` is `SET NULL`).
4. **A conversation has a title, computed rather than stored.**
   - `Conversation.Title` holds only a title the user chose.
   - The title shown is that one, or else the title of the most recent story linked to the
     conversation, or else the start of the user's first message.
   - So the default follows the latest story without any syncing, and a rename always wins.
5. **The history never shows the internals.** The detail endpoint returns only user and agent
   messages. System messages (instructions, saved context, failure notes) are left out, and
   `__END__CHAT__` is stripped from what the agent said.
6. **Storage is disclosed.**
   - A line in the chat says that conversations are saved so they can be picked up again or used
     for related stories, that our technical staff may see them to help, and that they can be
     deleted at any time.
   - It links to a `/privacy` page, which the footer also links to.
   - The page's text is a draft to be reviewed by someone qualified before launch.

## Consequences

### Positive
- A conversation that ended without a story, or with a failed one, can be picked up again.
- Parents are told what is kept and why, and they can delete it.

### Negative / trade-offs accepted
- Keeping every conversation means holding children's data for as long as the parent keeps it,
  for a feature (sequels) that does not exist yet. The disclosure and the delete action are what
  make that acceptable.
- Abandoned conversations are never cleaned up automatically.
- Sorting by the latest message needs a subquery per conversation. That is fine at this scale.

### Things this decision obliges us to build or watch
- The privacy page must be reviewed before launch.
- If sequels are never built, the reason for keeping completed conversations goes away. They
  should then be deleted once their story is created.
- Deleting a user (Clerk) already cascades to their conversations.

## Alternatives considered

### Delete a conversation once its story is created
This keeps the least data, but it removes the starting point for related stories. And
conversations still in progress or failed would be stored anyway, so the disclosure would be
needed in any case.

### Cascade deletes between stories and conversations
Deleting a finished story because its chat was deleted would surprise the parent, and a
conversation can be linked to more than one story.

### Store the default title on the conversation
It would need syncing every time a story is queued, and it would have to tell a user's rename
apart from a default.
