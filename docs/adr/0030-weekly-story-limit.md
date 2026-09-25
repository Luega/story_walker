# ADR 0030 — A weekly limit of seven finished stories

- **Status:** Accepted
- **Date:** 2026-09-25
- **Deciders:** Luca Martinelli

## Context

Every story costs one brief-authoring call and six image generations, plus rewrites
([ADR-0025](0025-rewrite-illustration-prompt-after-moderation.md)) and chat turns. Today nothing
limits how many a user makes. `User.Tier` exists, with `Free` as the only value, and was added so
that plans could gate things later.

## Decision

1. **A user on the `Free` plan can finish at most seven stories per calendar week.**
   - The week runs Monday 00:00 to Monday 00:00 **UTC**, since we do not know the user's time
     zone. The dashboard shows the reset in the browser's local time.
   - The limit is a function of the plan (`StoryQuota.WeeklyLimitFor`), so paid plans can set their
     own.
2. **Only finished stories count, and deleting one does not give the slot back.**
   - When the worker settles a story `Complete`, it appends a row to a new `CompletedGenerations`
     ledger: user, story, time. The row stays when the story is deleted; its `StoryId` becomes null.
   - Otherwise generating, deleting and generating again would get around the limit.
   - A failed story never reaches the ledger, so it costs the user nothing.
   - The migration backfills the ledger from existing `Complete` stories, so the current week
     counts correctly from the start.
3. **A story still generating holds a slot.**
   - Used = the ledger rows this week plus the user's stories in `Generating`.
   - Without that, six finished stories and two in flight would end at eight.
   - A generating story that fails gives its slot back, because it never reaches the ledger.
4. **At the limit, the chat stops before the model is called.**
   - `POST /stories/conversations` answers `429` with a ProblemDetails saying the weekly limit is
     reached and when it resets, before it spends a single token.
   - Reading conversations stays open.
5. **The dashboard shows what is left.** `GET /me/quota` returns the limit, how many are used, how
   many remain, and when the week resets. The dashboard shows "N of 7 stories left this week" and
   disables "+ New story" at zero.

## Consequences

### Positive
- The weekly cost of a free user is bounded.
- The user sees the limit before running into it, and learns when it resets.

### Negative / trade-offs accepted
- The check and the enqueue are not atomic. Two confirmations sent at the same moment from two
  tabs, with one slot left, can both get through. It is rare, costs at most one extra story, and
  is not worth a lock.
- The reset follows UTC: 01:00 or 02:00 on Monday in Italy, depending on daylight saving time.
- A user who reaches the limit mid-conversation cannot finish it until the week resets. The
  conversation is kept and can be resumed then.

### Things this decision obliges us to build or watch
- Paid plans will need their own limits in `StoryQuota.WeeklyLimitFor`.
- If users span several time zones, a per-user time zone could replace UTC.

## Alternatives considered

### A rolling seven-day window
Fairer and independent of time zone, but harder to explain than "resets every Monday". The user
chose the calendar week.

### Count the stories that still exist
No new table, but deleting stories would get around the limit.

### Block only at confirmation
The parent would plan a whole story before finding out, and we would pay for an interview that
cannot lead anywhere.
