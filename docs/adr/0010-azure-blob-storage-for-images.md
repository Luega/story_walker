# ADR 0010 — Azure Blob Storage for generated images

- **Status:** Accepted
- **Date:** 2026-06-09
- **Deciders:** Luca Martinelli

## Context

Generation produces binary image assets: per-page illustrations and per-character reference
images. These are large, numerous, served to browsers, and must not live in the relational
database. We need durable, cheap object storage with direct browser delivery.

## Decision

We will store all generated images in **Azure Blob Storage**. The database stores only
**URLs/keys** (`Page.image_url`, `Character.reference_image_url`); the bytes live in Blob.
Browsers fetch images directly from Blob (via CDN and/or time-limited **SAS URLs**), not
through the API.

## Consequences

### Positive
- Cheap, durable, virtually unlimited object storage.
- Offloads image delivery from the API; pairs naturally with a CDN.
- Clean separation: DB holds metadata + URLs, Blob holds bytes.

### Negative / trade-offs accepted
- Two stores to keep consistent (a deleted story must clean up its blobs).
- Access control needs care: private content served via SAS URLs or CDN with signed access.

### Things this decision obliges us to build or watch
- A container/naming convention (e.g., `stories/{storyId}/pages/{pageNo}.png`,
  `characters/{characterId}/reference.png`).
- SAS URL generation/expiry, or CDN signed URLs, for private delivery.
- Blob cleanup when stories/characters are deleted.

## Alternatives considered

### Store images as bytes in PostgreSQL (bytea / large objects)
One store, transactional. Rejected: bloats the DB, hurts backup/restore and query
performance, and is a poor fit for browser delivery.

### A third-party object store / image CDN service
Viable. Rejected: we are already on Azure; Blob + Azure CDN keeps the stack cohesive and
integrates with the rest of the infrastructure.
