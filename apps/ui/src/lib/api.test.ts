import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { worker } from "@/test/handlers";
import { aStory, aStorySummary } from "@/test/factories";
import {
  deleteStory,
  getStory,
  listStories,
  renameStory,
  sendAgentMessage,
} from "./api";

describe("listStories", () => {
  it("returns the summaries the API sends", async () => {
    const summary = aStorySummary({ title: "The Curious Moon" });
    worker.use(http.get("/api/stories", () => HttpResponse.json([summary])));

    await expect(listStories()).resolves.toEqual([summary]);
  });

  it("asks for a fresh list rather than a cached one", async () => {
    let cache: RequestCache | undefined;
    worker.use(
      http.get("/api/stories", ({ request }) => {
        cache = request.cache;
        return HttpResponse.json([]);
      }),
    );

    await listStories();

    // `cache: "no-store"` travels as the request's cache *mode*, not a Cache-Control header — the
    // dashboard polls, so serving it from cache would silently freeze the list.
    expect(cache).toBe("no-store");
  });

  it("throws when the API fails", async () => {
    worker.use(http.get("/api/stories", () => new HttpResponse(null, { status: 500 })));

    await expect(listStories()).rejects.toThrow(/500/);
  });
});

describe("getStory", () => {
  it("returns the story", async () => {
    const story = aStory();
    worker.use(http.get(`/api/stories/${story.id}`, () => HttpResponse.json(story)));

    await expect(getStory(story.id)).resolves.toEqual(story);
  });

  it("returns null for an unknown story instead of throwing", async () => {
    worker.use(http.get("/api/stories/:id", () => new HttpResponse(null, { status: 404 })));

    await expect(getStory("missing")).resolves.toBeNull();
  });

  it("throws on a server error", async () => {
    worker.use(http.get("/api/stories/:id", () => new HttpResponse(null, { status: 500 })));

    await expect(getStory("boom")).rejects.toThrow(/500/);
  });
});

describe("renameStory", () => {
  it("PATCHes the new title to the story's URL", async () => {
    const story = aStory({ title: "New title" });
    let method: string | undefined;
    let body: unknown;
    worker.use(
      http.patch(`/api/stories/${story.id}`, async ({ request }) => {
        method = request.method;
        body = await request.json();
        return HttpResponse.json(story);
      }),
    );

    await renameStory(story.id, "New title");

    expect(method).toBe("PATCH");
    expect(body).toEqual({ title: "New title" });
  });

  it("returns the title as the server stored it, not the one we sent", async () => {
    // The API trims; a client that echoed its own input would hide that from the user.
    const story = aStory({ title: "Clean title" });
    worker.use(http.patch("/api/stories/:id", () => HttpResponse.json(story)));

    await expect(renameStory(story.id, "  Clean title  ")).resolves.toBe("Clean title");
  });

  it("reports a missing story", async () => {
    worker.use(http.patch("/api/stories/:id", () => new HttpResponse(null, { status: 404 })));

    await expect(renameStory("missing", "x")).rejects.toThrow("Story not found.");
  });

  it("reports a rejected title", async () => {
    worker.use(http.patch("/api/stories/:id", () => new HttpResponse(null, { status: 400 })));

    await expect(renameStory("id", "")).rejects.toThrow(/400/);
  });
});

describe("deleteStory", () => {
  it("DELETEs the story", async () => {
    let method: string | undefined;
    worker.use(
      http.delete("/api/stories/:id", ({ request }) => {
        method = request.method;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await deleteStory("some-id");

    expect(method).toBe("DELETE");
  });

  it("treats 404 as success: the story is already gone", async () => {
    worker.use(http.delete("/api/stories/:id", () => new HttpResponse(null, { status: 404 })));

    await expect(deleteStory("missing")).resolves.toBeUndefined();
  });

  it("throws on a server error", async () => {
    worker.use(http.delete("/api/stories/:id", () => new HttpResponse(null, { status: 500 })));

    await expect(deleteStory("boom")).rejects.toThrow(/500/);
  });
});

describe("sendAgentMessage", () => {
  it("returns the agent's reply and the conversation id", async () => {
    worker.use(
      http.post("/api/stories/conversations", () =>
        HttpResponse.json({ conversationId: "conv-1", reply: "What a lovely idea!", briefReady: false }),
      ),
    );

    await expect(sendAgentMessage(null, "hello")).resolves.toEqual({
      kind: "reply",
      conversationId: "conv-1",
      reply: "What a lovely idea!",
    });
  });

  it("sends the conversation id on later turns", async () => {
    let body: unknown;
    worker.use(
      http.post("/api/stories/conversations", async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ conversationId: "conv-1", reply: "ok", briefReady: false });
      }),
    );

    await sendAgentMessage("conv-1", "  a dragon  ");

    expect(body).toEqual({ conversationId: "conv-1", message: "  a dragon  " });
  });

  it("reports a queued story when the brief is ready (202)", async () => {
    worker.use(
      http.post("/api/stories/conversations", () =>
        HttpResponse.json({ storyId: "story-9", status: "Generating" }, { status: 202 }),
      ),
    );

    await expect(sendAgentMessage("conv-1", "yes")).resolves.toEqual({
      kind: "queued",
      storyId: "story-9",
    });
  });

  it("reports an expired conversation (404)", async () => {
    worker.use(
      http.post("/api/stories/conversations", () => new HttpResponse(null, { status: 404 })),
    );

    await expect(sendAgentMessage("gone", "hello")).resolves.toEqual({ kind: "notFound" });
  });

  it("surfaces the ProblemDetails message from a failed turn", async () => {
    worker.use(
      http.post("/api/stories/conversations", () =>
        HttpResponse.json({ detail: "Invalid brief." }, { status: 502 }),
      ),
    );

    await expect(sendAgentMessage("conv-1", "yes")).resolves.toEqual({
      kind: "error",
      detail: "Invalid brief.",
    });
  });

  it("reports a network failure instead of throwing at the caller", async () => {
    worker.use(http.post("/api/stories/conversations", () => HttpResponse.error()));

    await expect(sendAgentMessage(null, "hello")).resolves.toEqual({
      kind: "error",
      detail: "Could not reach the server.",
    });
  });
});
