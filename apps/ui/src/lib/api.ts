// Client for the StoryWalker API. Types mirror the wire DTOs in
// src/StoryWalker.Application/Api (StoryDtos.cs, AgentDtos.cs, ConversationDtos.cs); JSON is
// camelCase.
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

export type ConversationStatus = "Active" | "Completed";

/** One row of the conversation history (ADR-0028); the title is computed by the API. */
export interface ConversationSummary {
  id: string;
  title: string;
  status: ConversationStatus;
  /** True while the conversation can still be continued. */
  resumable: boolean;
  /** True when the latest story made from it failed (ADR-0027). */
  storyFailed: boolean;
  lastActivityAt: string;
  /** The newest finished story made from it, to build a related one on (ADR-0029). */
  latestCompleteStoryId: string | null;
}

/** Only what the parent and the agent said: the API leaves out system messages and the sentinel. */
export interface ConversationMessage {
  role: "user" | "assistant";
  text: string;
}

export interface Conversation {
  id: string;
  title: string;
  status: ConversationStatus;
  resumable: boolean;
  messages: ConversationMessage[];
  latestCompleteStoryId: string | null;
}

/** Where the user stands against the weekly story limit (ADR-0030). */
export interface Quota {
  limit: number;
  /** Finished this week, plus the stories still generating, which hold a slot. */
  used: number;
  remaining: number;
  /** The next Monday 00:00 UTC. */
  resetsAt: string;
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
  /** Starts a new conversation from one of the user's finished stories (ADR-0029). */
  basedOnStoryId?: string,
): Promise<AgentTurn> {
  let res: Response;
  try {
    res = await fetch(`${BASE}/stories/conversations`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ conversationId, message, basedOnStoryId }),
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

  // ProblemDetails (502 invalid brief, 503 no user, 400 validation, 422 content filter) carry a
  // human-readable detail.
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

/** The signed-in user's conversations, most recent activity first (ADR-0028). */
export async function listConversations(): Promise<ConversationSummary[]> {
  // Fresh every time: the sidebar reopens after a chat that may have changed the list.
  const res = await fetch(`${BASE}/stories/conversations`, { cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load your conversations (${res.status})`);
  return res.json();
}

/** One conversation as the chat shows it, or null when it is unknown (or not the user's). */
export async function getConversation(id: string): Promise<Conversation | null> {
  const res = await fetch(`${BASE}/stories/conversations/${encodeURIComponent(id)}`, {
    cache: "no-store",
  });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`Failed to load the conversation (${res.status})`);
  return res.json();
}

/** Sets the user's own title. Returns the title as the server stored it (it trims). */
export async function renameConversation(id: string, title: string): Promise<string> {
  const res = await fetch(`${BASE}/stories/conversations/${encodeURIComponent(id)}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
  if (res.status === 404) throw new Error("Conversation not found.");
  if (!res.ok) throw new Error(`Failed to rename the conversation (${res.status})`);
  const summary: ConversationSummary = await res.json();
  return summary.title;
}

/**
 * Deletes a conversation and its messages; the stories made from it are kept (ADR-0028). A 404
 * counts as success: the conversation is already gone.
 */
export async function deleteConversation(id: string): Promise<void> {
  const res = await fetch(`${BASE}/stories/conversations/${encodeURIComponent(id)}`, {
    method: "DELETE",
  });
  if (!res.ok && res.status !== 404) {
    throw new Error(`Failed to delete the conversation (${res.status})`);
  }
}

/** Where the user stands against the weekly story limit (ADR-0030). */
export async function getQuota(): Promise<Quota> {
  // Fresh every time: it changes as stories finish, and the dashboard asks again when they do.
  const res = await fetch(`${BASE}/me/quota`, { cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load your weekly limit (${res.status})`);
  return res.json();
}
