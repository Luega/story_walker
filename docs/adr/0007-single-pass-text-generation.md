# ADR 0007 — Single-pass text generation for narrative coherence

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

## Context

A story of up to 10 pages must read as one coherent narrative: consistent characters, an
arc that builds, and no contradictions between pages. An early idea — generating each page's
text in parallel with independent model calls — was rejected because page N must _continue_
page N−1; independent calls cannot know what the others wrote and produce an incoherent story.

## Decision

We will generate the **entire story text in a single Foundry pass**. This pass happens **inside the
agent's structured generation brief** ([ADR-0013](0013-agent-authored-generation-brief.md)):
the agent authors all page texts together, so the worker never generates text — it receives
the per-page text in the brief.

## Consequences

### Positive

- Maximum narrative coherence: one model invocation sees the whole arc.
- Fewest text calls (cheapest, simplest) for short kids' stories (≤10 pages).
- A clean hand-off: the per-page text is already in the brief, persisted to `Page` rows before
  images begin.

### Negative / trade-offs accepted

- Bounded by the model's output/context limits — fine for ≤10 short pages, but not a path to
  very long content.
- If brief authoring fails, the agent regenerates the whole brief — cheap, and it happens
  before the expensive image stage even starts.

### Things this decision obliges us to build or watch

- The brief's structured output already carries one entry per page; brief validation
  ([ADR-0013](0013-agent-authored-generation-brief.md)) checks the page entries are coherent
  (correct count, sequential `pageNo`).

## Alternatives considered

### Parallel per-page text generation

Fast and embarrassingly parallel. Rejected: breaks narrative continuity — the core quality
requirement of a story.

### Sequential page-by-page chaining (each call fed prior pages/summary)

Coherent and scales to longer stories. Rejected as the default: more calls, more latency,
and unnecessary for short stories. Documented as the upgrade path if we later support long
or chaptered stories.
