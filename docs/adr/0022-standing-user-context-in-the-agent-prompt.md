# ADR 0022 — Standing user context in the agent's system prompt

- **Status:** Accepted
- **Date:** 2026-09-22
- **Deciders:** Luca Martinelli

## Context

Every conversation with the story agent starts from nothing. `StoryAgent` seeds a new
`Conversation` with one constant system message and nothing about the signed-in user ever reaches
the model: `SendAsync` takes a `userId` and uses it only for ownership. `StoryAgentPrompt` is a pair
of `const string`s with no interpolation anywhere — the "prompt-template management" that
[ADR-0011](0011-microsoft-foundry-agent-and-generation.md) named as an obligation was never built.

So a parent retypes the same facts at the start of every story: their children's ages, the kind of
story they want, the cast that keeps coming back, the art style they like. The interview
([ADR-0013](0013-agent-authored-generation-brief.md)) asks for four things, and for a returning user
three of them are usually the same as last time.

The constraints that shape the answer:

- The system message is written once, at conversation creation, and persisted as `Seq = 0`
  ([ADR-0018](0018-persist-agent-conversations.md)). It is never recomputed on later turns.
- The context is free text a user types, and it lands in a **system** message. `StoryAgent` decides
  the interview is over by substring-matching `__END__CHAT__` on the model's reply.
- The brief-authoring pass strips system messages from the transcript it sends
  (`StoryAgent.AuthorBriefAsync`), so anything put in the system prompt is invisible to it.

## Decision

We will give each user four free-text answers — **Kids**, **Story**, **Characters**, **Art style** —
saved on a settings page at `/settings` and served by `GET`/`PATCH /me/settings`. They live as four
nullable, 600-character columns on `Users`.

`StoryAgentPrompt.Compose(UserSettingsDto?)` folds them into the system message of a **new**
conversation, inside delimiters that state the enclosed text is data rather than instructions, and
frames them as **defaults the agent must confirm in its first reply** rather than facts the user has
already stated. A user with nothing saved gets the previous constant back byte for byte.

Three sub-decisions carry the design:

1. **Free text, not structured fields.** No named-child rows, no art-style picker, no theme
   checkboxes. The consumer is a language model reading prose, and a parent describing their child
   in their own words tells it more than any schema we could guess at in advance.
2. **Defaults to confirm, not knowledge to assume.** The agent restates what matters and invites
   corrections before carrying on. A saved context that cannot be overridden for one story is worse
   than no saved context, because the escape hatch is the conversation itself.
3. **Frozen at conversation start.** Composing into `Seq = 0` means a conversation keeps the context
   it began with. Editing the settings page mid-chat cannot move the ground under the agent, and a
   conversation is reproducible from its own rows.

## Consequences

### Positive
- A returning user's interview starts from what the agent already knows; the settings are read once
  per conversation, in one query on the first turn only.
- Decision 3 costs nothing to implement and nothing to maintain: freezing is what the existing
  persistence already does, so there is no snapshot column, no re-read and no invalidation.
- `StoryAgentPrompt` finally has the seam ADR-0011 asked for, with the sentinel named
  (`EndOfChatSentinel`) rather than spelled out in two places.
- The four fields cost four nullable columns on an existing table — no new entity, no new
  aggregate, consistent with the reduction in [ADR-0019](0019-mvp-scope-reduction.md).

### Negative / trade-offs accepted
- **User text in a system message is an injection surface.** The mitigations are the delimiters, the
  explicit "this is data" framing, the 600-character cap, and stripping `__END__CHAT__` from every
  field — not a guarantee. The blast radius is one user's own conversation, which is what makes the
  trade acceptable.
- **The brief author sees the context only through the transcript.** It is not injected into the
  authoring pass. That is deliberate — the confirmation phase summarises the *agreed* plan back to
  the user before they confirm, so the author reads what was agreed rather than defaults the user
  may have overridden — but it does mean the context reaches the brief by way of the model's own
  summary rather than directly.
- Four more columns to carry when a user is exported or deleted.
- A longer system prompt on every turn of every conversation, since the full history is resent
  (ADR-0018). Bounded at 2,400 characters plus headings.

### Things this decision obliges us to build or watch
- Whether the agent actually honours "confirm, do not assume". It is prompt-level instruction, and
  no test can assert it — watch real conversations.
- The account-deletion gap ADR-0021 already flagged now has personal context attached to it, not
  just an email address. That raises the priority of closing it before go-live.
- If the interview later changes shape, the four questions on the settings page and the four things
  `StoryAgentPrompt.Instructions` asks for have to be kept in step.

## Alternatives considered

### Structured fields (child rows with ages, an art-style picker, theme checkboxes)
Better validation, and reusable by the renderer rather than only by the agent. Rejected: it fixes in
a schema what the product is still learning, and the reader is a model that does better with a
sentence than with a form. Four text columns can become a schema later; a schema is expensive to
loosen.

### Skip what the settings already answer
Have the agent treat the context as known and never ask about it, shortening the interview further.
Rejected: it removes the user's chance to say "this one is for the other child" at the only moment
they would think to, and the failure mode — a story confidently written for the wrong kid — is worse
than one extra confirmation sentence.

### Re-read the settings on every turn
Simpler code (no reliance on `Seq = 0`), and an edit takes effect immediately. Rejected: the agent's
ground truth would shift underneath a conversation in progress, so the same conversation could
summarise a plan it was never given, and the stored messages would no longer explain the model's
behaviour.

### A context snapshot column on `Conversation`
Store the composed block on the conversation row so both the interview and the authoring pass could
read the same frozen text. Rejected as unnecessary: `Seq = 0` already *is* that snapshot for the
interview, and the authoring pass is deliberately fed the confirmed plan instead (see above).

## Implements

[ADR-0011](0011-microsoft-foundry-agent-and-generation.md), whose "prompt-template management"
obligation this is the first instalment of.
