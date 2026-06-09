# ADR 0007 — Single-pass text generation for narrative coherence

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Story Walker team

## Context

A story of up to 10 pages must read as one coherent narrative: consistent characters, an
arc that builds, and no contradictions between pages. An early idea — generating each page's
text in parallel with independent model calls — was rejected because page N must *continue*
page N−1; independent calls cannot know what the others wrote and produce an incoherent story.

## Decision

We will generate the **entire story text in a single Foundry call**, grounded in the bible
(characters, style, outline), and then **split the result into pages**. The text stage is
therefore **sequential** in the pipeline (it is step 2, before any per-page image fan-out).

## Consequences

### Positive
- Maximum narrative coherence: one model invocation sees the whole arc.
- Fewest text calls (cheapest, simplest) for short kids' stories (≤10 pages).
- A clean hand-off: text exists and is split into `Page` rows before images begin.

### Negative / trade-offs accepted
- Bounded by the model's output/context limits — fine for ≤10 short pages, but not a path to
  very long content.
- A single failure regenerates all text (acceptable; text is one cheap step relative to images).

### Things this decision obliges us to build or watch
- A reliable way to split the generated text into exactly `page_count` pages (e.g., a
  structured/JSON response with one entry per page).
- A guardrail if the model returns the wrong number of pages.

## Alternatives considered

### Parallel per-page text generation
Fast and embarrassingly parallel. Rejected: breaks narrative continuity — the core quality
requirement of a story.

### Sequential page-by-page chaining (each call fed prior pages/summary)
Coherent and scales to longer stories. Rejected as the default: more calls, more latency,
and unnecessary for short stories. Documented as the upgrade path if we later support long
or chaptered stories.
