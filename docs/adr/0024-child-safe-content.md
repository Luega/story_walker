# ADR 0024 — Child-safe content: prompt rules and a friendly content-filter answer

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Luca Martinelli

## Context

Story Walker writes and illustrates stories for children, but until now nothing in the code said
so. Asking for violent or sexual stories was blocked anyway, by things outside the repo:

- the **Azure AI Foundry content filter** attached to every deployment, which by default blocks
  hate, sexual, violence and self-harm content of *medium* severity or above, in both the prompt
  and the completion;
- the chat model's own training, which often declines in plain text;
- the image model's filter, whose refusals already mark a page `Failed` without a retry
  ([ADR-0005](0005-idempotency-and-failure-handling.md)).

That leaves two gaps. The default filter is set for a general adult audience, so content rated
low severity (a frightening scene, a fight, a disturbing theme) passes, and nothing asks the model
to write for the child's age. And when the chat filter does fire, Azure answers `400` with code
`content_filter`. `POST /stories/conversations` did not recognise that error, so the parent got a
generic `500` instead of being told to rephrase.

## Decision

We will state the audience in both system prompts, and turn a content-filter refusal into an
answer the parent can act on.

1. `StoryAgentPrompt.Instructions` and `BriefAuthoringInstructions` each carry a **content rule**.
   Everything has to suit the child's age. Nothing sexual, no graphic violence or gore, no
   self-harm, no hateful content, and nothing meant to frighten or upset. When a request asks for
   any of these, the interviewer declines briefly and kindly and suggests a gentler direction,
   instead of refusing outright or going along with it.
2. `StoryAgent` translates a content-filter refusal from the chat model into
   `ContentFilteredException`. That covers a `400` whose error is `content_filter` and a reply
   whose finish reason is `ContentFilter`. It throws before `SaveChanges`, so the turn is rolled
   back and the refused message never enters the stored history. If it stayed there, every later
   turn would resend it and be refused again.
3. The endpoint maps that exception to **`422`** with a ProblemDetails `detail` asking the parent
   to rephrase. The UI already shows `detail` for any ProblemDetails, so it needs no change.

## Consequences

### Positive
- Requests the default filter lets through, such as scary or mildly violent ones, now meet an
  instruction that asks for age-appropriate content.
- A blocked message gets a clear, friendly answer, and the conversation carries on.

### Negative / trade-offs accepted
- A prompt rule is guidance, not enforcement. A determined user can still steer the model, and
  the provider's filter remains the only hard block.
- The refusal is detected by the error code in the provider's response. If Azure changes that
  code, refusals fall back to the generic `500`.

### Things this decision obliges us to build or watch
- The Foundry content filter on the chat and image deployments is still configured only in the
  portal. A stricter custom filter (blocking *low* severity) should be created, and recorded in
  `iac/`, so that nobody can loosen it without anyone noticing.
- Switching images to Flux ([ADR-0011](0011-microsoft-foundry-agent-and-generation.md)) swaps
  Azure's image filter for Black Forest Labs' moderation, which behaves differently.

## Alternatives considered

### A separate moderation call on every message
Calling a moderation model or Azure AI Content Safety before each turn would catch more, but it
adds latency and cost to every message, and a new dependency. The deployment's filter already runs
the same classifiers on every request. The stricter custom filter above is the cheaper way to
tighten them.

### Answering a refusal with `200` and a canned reply
This would make the refusal look like part of the chat. But the refused turn is not persisted, so a
brand-new conversation would have no id to return, and a `200` would hide from the client that
nothing was saved.
