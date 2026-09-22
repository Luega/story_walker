// Client for the StoryWalker API. Types mirror the wire DTOs in
// src/StoryWalker.Application/Api (StoryDtos.cs, AgentDtos.cs); JSON is camelCase.
// Requests go through the /api rewrite in next.config.ts.

export type StoryStatus = "Draft" | "Generating" | "Complete" | "Failed";
export type PageStatus = "Pending" | "Complete" | "Failed";

export interface StorySummary {
  id: string;
  title: string;
  status: StoryStatus;
  createdAt: string;
}

export interface Page {
  pageNo: number;
  content: string;
  imageUrl: string | null;
  status: PageStatus;
}

export interface Story {
  id: string;
  title: string;
  targetAge: number;
  status: StoryStatus;
  artStyle: string;
  pages: Page[];
}

/**
 * The four free-text answers a user saves once, which the story agent reads as defaults at the
 * start of every new conversation. Null is "not answered", which is why it is not `string`.
 */
export interface UserSettings {
  kids: string | null;
  story: string | null;
  characters: string | null;
  artStyle: string | null;
}

export type AgentTurn =
  | { kind: "reply"; conversationId: string; reply: string }
  | { kind: "queued"; storyId: string }
  | { kind: "notFound" }
  | { kind: "error"; detail: string };

const BASE = "/api";

export async function listStories(): Promise<StorySummary[]> {
  const res = await fetch(`${BASE}/stories`, { cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load stories (${res.status})`);
  return res.json();
}

/** Returns null when the story does not exist. */
export async function getStory(id: string): Promise<Story | null> {
  const res = await fetch(`${BASE}/stories/${encodeURIComponent(id)}`, { cache: "no-store" });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`Failed to load story (${res.status})`);
  return res.json();
}

/**
 * Renames a story. Returns the title as the server stored it (it trims), so the caller shows what
 * was actually persisted rather than the raw input.
 */
export async function renameStory(id: string, title: string): Promise<string> {
  const res = await fetch(`${BASE}/stories/${encodeURIComponent(id)}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
  if (res.status === 404) throw new Error("Story not found.");
  if (!res.ok) throw new Error(`Failed to rename story (${res.status})`);
  const story: Story = await res.json();
  return story.title;
}

/** Deletes a story and its pages. A 404 counts as success: the story is already gone. */
export async function deleteStory(id: string): Promise<void> {
  const res = await fetch(`${BASE}/stories/${encodeURIComponent(id)}`, { method: "DELETE" });
  if (!res.ok && res.status !== 404) {
    throw new Error(`Failed to delete story (${res.status})`);
  }
}

/**
 * Sends one chat turn to the story agent. 200 is an ordinary reply; 202 means the brief was
 * authored and the story is queued for generation; 404 means the conversation is gone.
 */
export async function sendAgentMessage(
  conversationId: string | null,
  message: string,
): Promise<AgentTurn> {
  let res: Response;
  try {
    res = await fetch(`${BASE}/stories/conversations`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ conversationId, message }),
    });
  } catch {
    return { kind: "error", detail: "Could not reach the server." };
  }

  if (res.status === 202) {
    const body = await res.json();
    return { kind: "queued", storyId: body.storyId };
  }
  if (res.ok) {
    const body = await res.json();
    return { kind: "reply", conversationId: body.conversationId, reply: body.reply };
  }
  if (res.status === 404) return { kind: "notFound" };

  // ProblemDetails (502 invalid brief, 503 no user, 400 validation) carry a human-readable detail.
  const problem = await res.json().catch(() => null);
  return {
    kind: "error",
    detail: problem?.detail ?? problem?.title ?? `Server error (${res.status})`,
  };
}

export async function getUserSettings(): Promise<UserSettings> {
  const res = await fetch(`${BASE}/me/settings`, { cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load your settings (${res.status})`);
  return res.json();
}

/**
 * Saves all four answers at once — the form always submits the whole set, so an answer the user
 * cleared arrives blank and is cleared. Returns them as the server stored them (it trims), so the
 * form can show what was actually persisted rather than the raw input.
 */
export async function saveUserSettings(settings: UserSettings): Promise<UserSettings> {
  const res = await fetch(`${BASE}/me/settings`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(settings),
  });

  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(
      firstValidationError(problem) ??
        problem?.detail ??
        `Failed to save your settings (${res.status})`,
    );
  }

  return res.json();
}

/**
 * ASP.NET's ValidationProblem puts the readable message inside `errors`, keyed by field, and
 * leaves `detail` unset — so the one thing worth showing the user is the one thing a naive
 * `detail ?? title` would miss.
 */
function firstValidationError(problem: unknown): string | undefined {
  const errors = (problem as { errors?: Record<string, string[]> } | null)?.errors;
  return errors ? Object.values(errors).flat()[0] : undefined;
}
