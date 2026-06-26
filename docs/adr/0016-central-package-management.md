# ADR 0016 — Central Package Management for NuGet versions

- **Status:** Accepted
- **Date:** 2026-06-26
- **Deciders:** Luca Martinelli

## Context

The monorepo ([ADR-0012](0012-monorepo.md)) now holds several .NET projects — the three shared
libraries ([ADR-0015](0015-layered-architecture.md)), the `apps/api` host, and the
`tests/StoryWalker.Api.Tests` integration suite — and the list will grow (`apps/functions`, more
test projects). With per-project `<PackageReference Version="…">`, the same package can be pinned to
different versions in different `.csproj` files.

This already bit us: adding the test project surfaced an `MSB3277` conflict because the EF Core
provider (`Npgsql.EntityFrameworkCore.PostgreSQL`) floors `Microsoft.EntityFrameworkCore.Relational`
at a lower patch (10.0.4) than the `Microsoft.EntityFrameworkCore` we pin directly (10.0.9), and the
two versions met in the test project. The first fix was a hand-added explicit reference — a
per-project patch that does not prevent the next drift. We want one place that decides every package
version, so upgrades are a single edit and version skew is structurally impossible.

Note that the conflicting package (`EF Core Relational`) is reached **transitively**, so plain CPM
does not unify it: CPM only pins packages a project references **directly**. We verified this — with
CPM on but transitive pinning off, removing the explicit reference brings `MSB3277` straight back.

## Decision

We will adopt **NuGet Central Package Management (CPM)**: a single
[`Directory.Packages.props`](../../Directory.Packages.props) at the repo root sets
`ManagePackageVersionsCentrally=true` and declares every version via `<PackageVersion>`. Project
files keep `<PackageReference Include="…">` **without** a `Version` attribute — the version is
resolved centrally.

We also enable `CentralPackageTransitivePinningEnabled=true` so the central versions apply to
**transitive** dependencies too. This is what actually unifies the `EF Core Relational` conflict to
10.0.9 — and it lets us drop the explicit `Microsoft.EntityFrameworkCore.Relational` reference the
test project previously needed.

Per-project specifics that are *not* versions stay in the `.csproj` (e.g. the
`PrivateAssets`/`IncludeAssets` on `Microsoft.EntityFrameworkCore.Design`, `OutputType` for the
xUnit v3 test project).

## Consequences

### Positive
- **One source of truth** for versions; upgrading a package is one edit in `Directory.Packages.props`.
- Version skew across projects is eliminated by construction — the class of `MSB3277` conflict we
  hit cannot recur.
- New projects inherit the pinned versions automatically; `.csproj` files get shorter and reviews of
  dependency changes focus on a single file.

### Negative / trade-offs accepted
- A project that genuinely needs a different version must opt out explicitly (`VersionOverride` on
  the reference), which is slightly less obvious than an inline version.
- One more root-level build file to understand; contributors must know that versions live in
  `Directory.Packages.props`, not the `.csproj`.

### Things this decision obliges us to build or watch
- Add every new package's version to `Directory.Packages.props`; a bare `<PackageReference>` with no
  central entry fails the build (a useful guardrail).
- Keep transitively-floored packages (like `Microsoft.EntityFrameworkCore.Relational`) pinned
  centrally so provider/runtime versions stay aligned. Transitive pinning means a `<PackageVersion>`
  here can silently *raise* a transitive dependency — watch upgrades for unintended bumps.

## Alternatives considered

### Per-project `<PackageReference Version="…">` (status quo)
The default. Rejected: it is exactly what produced the version conflict, and the problem grows with
each new project; keeping versions in sync by hand across the monorepo is error-prone.

### A shared MSBuild props file with version *properties* (e.g. `$(EfCoreVersion)`)
Versions defined once as MSBuild properties and referenced from each `.csproj`. Viable and used
before CPM existed, but it is a homegrown convention with no tooling support; CPM is the
first-class, NuGet-native mechanism (restore-aware, IDE-aware) and is the current recommended
practice.

### Do nothing but pin the one conflicting package
An explicit `Microsoft.EntityFrameworkCore.Relational` reference in the affected project (the first
fix we tried). Rejected as the end state: it patches this single conflict reactively without
preventing the next one, and `CentralPackageTransitivePinningEnabled` solves the whole class of
transitive conflicts centrally — so the explicit reference was removed.
