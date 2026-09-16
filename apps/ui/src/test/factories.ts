import type { Page, Story, StorySummary } from "@/lib/api";

// Builders so each test states only the fields it actually cares about; everything else gets a
// plausible default and stays out of the way.

let seq = 0;
const nextId = () => `00000000-0000-4000-8000-${String(++seq).padStart(12, "0")}`;

export function aStorySummary(overrides: Partial<StorySummary> = {}): StorySummary {
  return {
    id: nextId(),
    title: "Il drago gentile",
    status: "Complete",
    createdAt: "2026-09-01T10:00:00Z",
    ...overrides,
  };
}

export function aPage(overrides: Partial<Page> = {}): Page {
  return {
    pageNo: 1,
    content: "C'era una volta un drago che aveva paura del buio.",
    imageUrl: "https://example.test/page-1.png",
    status: "Complete",
    ...overrides,
  };
}

export function aStory(overrides: Partial<Story> = {}): Story {
  return {
    id: nextId(),
    title: "Il drago gentile",
    targetAge: 5,
    status: "Complete",
    artStyle: "acquerello",
    pages: [aPage({ pageNo: 1 }), aPage({ pageNo: 2 })],
    ...overrides,
  };
}
