# ADR 0036 — Changes reach main through pull requests, gated by one required check

- **Status:** Proposed
- **Date:** 2026-10-15
- **Deciders:** Luca Martinelli

## Context

The repository is public and meant to become open source. Until now every change was pushed
straight to `main`, and a push to `main` deploys to prod ([ADR-0035](0035-single-lean-prod-and-github-actions.md)):
nothing stops an untested commit from reaching prod but the deploy workflow's own CI run.

A branch ruleset can require a pull request and passing status checks before a merge. But the
three CI workflows (`api.yml`, `worker.yml`, `ui.yml`) are filtered by path on a pull request, as
ADR-0012 planned: a required check whose workflow does not run on a pull request stays "Expected"
forever, and the pull request can never be merged.

The deploy workflow has the opposite problem: it calls all three CI workflows on every run, since a
path filter applies only to the events that start a workflow, never to `workflow_call`. A change to
a Terraform variable runs every test.

## Decision

- **`main` takes changes only through pull requests.** A branch ruleset on the default branch
  requires a pull request with no approvals (the only maintainer cannot approve their own), requires
  the `ci-passed` check, blocks force pushes and deletion, and has no bypass list.
- **One CI entry point, `ci.yml`**, runs on every pull request and is what `deploy.yml` calls. Its
  first job lists the changed files with `git diff` and decides which component workflows run; the
  path lists that used to sit on each workflow's `pull_request` trigger move there. The component
  workflows become `workflow_call` only.
- **`ci-passed` is the only required check.** It runs after the others whatever happens, and fails if
  any of them failed or was cancelled; a skipped workflow is not a failure.
- **What counts as changed:** on a pull request, the files that differ from its base; on a push, the
  files the push changed. When that cannot be known (a manual run, a first push, a rewritten
  history), everything runs.

## Consequences

### Positive
- Every change on `main` has passed the CI that concerns it, on a pull request, before it deploys.
- A pull request that touches only documentation or `iac/` merges without waiting for tests, and a
  deploy runs only the tests of what changed.
- The ruleset names one check; adding or renaming a workflow does not touch it.
- No third-party action decides what runs: the filter is a few lines of shell in the repository.

### Negative / trade-offs accepted
- A deploy of an `iac/`-only change builds the images without running their tests. The code in them
  is the code already tested when it reached `main`.
- Every change, even a one-line fix, needs a branch and a pull request.
- The path lists live in `ci.yml`, away from the workflows they gate.

## Alternatives considered

### Require each component's check
Breaks on every pull request that does not touch that component, as described above.

### Drop the path filters, run everything on every pull request
Simple and always correct, but a documentation change waits several minutes for tests that cannot
fail because of it.

### A path-filter action (e.g. `dorny/paths-filter`)
Well known, but a third-party action with the repository's token, for what `git diff` and `grep` do.
