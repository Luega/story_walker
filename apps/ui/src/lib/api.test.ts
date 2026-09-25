import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { worker } from "@/test/handlers";
import {
  aConversation,
  aConversationSummary,
  aStory,
  aStorySummary,
  aUserSettings,
} from "@/test/factories";
import type { UserSettings } from "./api";
import {
  getQuota,
  deleteConversation,
  deleteStory,
  getConversation,
  listConversations,
  renameConversation,
  getStory,
  getUserSettings,
  listStories,
  renameStory,
  saveUserSettings,
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

  // A related story (ADR-0029): the first turn names the story it builds on.
  it("names the story to build on when asked to", async () => {
    let body: unknown;
    worker.use(
      http.post("/api/stories/conversations", async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ conversationId: "conv-1", reply: "ok", briefReady: false });
      }),
    );

    await sendAgentMessage(null, "a sequel", "story-7");

    expect(body).toEqual({ conversationId: null, message: "a sequel", basedOnStoryId: "story-7" });
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

describe("getUserSettings", () => {
  it("returns the saved context", async () => {
    const settings = aUserSettings({ kids: "Mia is 5 and loves dinosaurs" });
    worker.use(http.get("/api/me/settings", () => HttpResponse.json(settings)));

    await expect(getUserSettings()).resolves.toEqual(settings);
  });

  it("asks for a fresh copy rather than a cached one", async () => {
    let cache: RequestCache | undefined;
    worker.use(
      http.get("/api/me/settings", ({ request }) => {
        cache = request.cache;
        return HttpResponse.json(aUserSettings());
      }),
    );

    await getUserSettings();

    expect(cache).toBe("no-store");
  });

  it("throws when the API fails", async () => {
    worker.use(http.get("/api/me/settings", () => new HttpResponse(null, { status: 500 })));

    await expect(getUserSettings()).rejects.toThrow(/500/);
  });
});

describe("saveUserSettings", () => {
  it("sends every field, so a cleared answer is cleared", async () => {
    let body: UserSettings | undefined;
    worker.use(
      http.patch("/api/me/settings", async ({ request }) => {
        body = (await request.json()) as UserSettings;
        return HttpResponse.json(body);
      }),
    );

    await saveUserSettings(aUserSettings({ kids: "Mia is 5", story: null }));

    expect(body).toEqual({
      kids: "Mia is 5",
      story: null,
      characters: "Pip, a small grey kitten",
      artStyle: "soft watercolour",
    });
  });

  it("returns what the server stored, not what was sent", async () => {
    const stored = aUserSettings({ kids: "Mia is 5" });
    worker.use(http.patch("/api/me/settings", () => HttpResponse.json(stored)));

    await expect(saveUserSettings(aUserSettings({ kids: "  Mia is 5  " }))).resolves.toEqual(
      stored,
    );
  });

  // ASP.NET's ValidationProblem carries no `detail`; the message the user needs to read is inside
  // `errors`, so it has to be dug out rather than replaced with a bare status code.
  it("surfaces the validation message when an answer is too long", async () => {
    worker.use(
      http.patch("/api/me/settings", () =>
        HttpResponse.json(
          {
            title: "One or more validation errors occurred.",
            status: 400,
            errors: { artStyle: ["Keep this to 600 characters or fewer."] },
          },
          { status: 400 },
        ),
      ),
    );

    await expect(saveUserSettings(aUserSettings())).rejects.toThrow(
      /Keep this to 600 characters or fewer/,
    );
  });

  it("throws on a server error", async () => {
    worker.use(http.patch("/api/me/settings", () => new HttpResponse(null, { status: 500 })));

    await expect(saveUserSettings(aUserSettings())).rejects.toThrow(/500/);
  });
});

describe("listConversations", () => {
  it("returns the history the API sends", async () => {
    const summary = aConversationSummary({ title: "Bedtime ideas" });
    worker.use(http.get("/api/stories/conversations", () => HttpResponse.json([summary])));

    await expect(listConversations()).resolves.toEqual([summary]);
  });

  it("asks for a fresh list rather than a cached one", async () => {
    let cache: RequestCache | undefined;
    worker.use(
      http.get("/api/stories/conversations", ({ request }) => {
        cache = request.cache;
        return HttpResponse.json([]);
      }),
    );

    await listConversations();

    expect(cache).toBe("no-store");
  });

  it("throws when the API fails", async () => {
    worker.use(http.get("/api/stories/conversations", () => new HttpResponse(null, { status: 500 })));

    await expect(listConversations()).rejects.toThrow(/500/);
  });
});

describe("getConversation", () => {
  it("returns the conversation", async () => {
    const conversation = aConversation();
    worker.use(
      http.get(`/api/stories/conversations/${conversation.id}`, () =>
        HttpResponse.json(conversation),
      ),
    );

    await expect(getConversation(conversation.id)).resolves.toEqual(conversation);
  });

  it("returns null for an unknown conversation instead of throwing", async () => {
    worker.use(
      http.get("/api/stories/conversations/:id", () => new HttpResponse(null, { status: 404 })),
    );

    await expect(getConversation("missing")).resolves.toBeNull();
  });

  it("throws on a server error", async () => {
    worker.use(
      http.get("/api/stories/conversations/:id", () => new HttpResponse(null, { status: 500 })),
    );

    await expect(getConversation("boom")).rejects.toThrow(/500/);
  });
});

describe("renameConversation", () => {
  it("PATCHes the new title and returns the title the server stored", async () => {
    let body: unknown;
    worker.use(
      http.patch("/api/stories/conversations/:id", async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(aConversationSummary({ title: "Clean title" }));
      }),
    );

    await expect(renameConversation("id", "  Clean title  ")).resolves.toBe("Clean title");
    expect(body).toEqual({ title: "  Clean title  " });
  });

  it("reports a missing conversation", async () => {
    worker.use(
      http.patch("/api/stories/conversations/:id", () => new HttpResponse(null, { status: 404 })),
    );

    await expect(renameConversation("missing", "x")).rejects.toThrow("Conversation not found.");
  });
});

describe("deleteConversation", () => {
  it("DELETEs the conversation", async () => {
    let method: string | undefined;
    worker.use(
      http.delete("/api/stories/conversations/:id", ({ request }) => {
        method = request.method;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await deleteConversation("some-id");

    expect(method).toBe("DELETE");
  });

  it("treats 404 as success: the conversation is already gone", async () => {
    worker.use(
      http.delete("/api/stories/conversations/:id", () => new HttpResponse(null, { status: 404 })),
    );

    await expect(deleteConversation("missing")).resolves.toBeUndefined();
  });

  it("throws on a server error", async () => {
    worker.use(
      http.delete("/api/stories/conversations/:id", () => new HttpResponse(null, { status: 500 })),
    );

    await expect(deleteConversation("boom")).rejects.toThrow(/500/);
  });
});

describe("getQuota", () => {
  it("returns where the user stands against the weekly limit", async () => {
    const quota = { limit: 7, used: 2, remaining: 5, resetsAt: "2026-09-28T00:00:00Z" };
    worker.use(http.get("/api/me/quota", () => HttpResponse.json(quota)));

    await expect(getQuota()).resolves.toEqual(quota);
  });

  it("asks for a fresh count rather than a cached one", async () => {
    let cache: RequestCache | undefined;
    worker.use(
      http.get("/api/me/quota", ({ request }) => {
        cache = request.cache;
        return HttpResponse.json({ limit: 7, used: 0, remaining: 7, resetsAt: "2026-09-28T00:00:00Z" });
      }),
    );

    await getQuota();

    expect(cache).toBe("no-store");
  });

  it("throws when the API fails", async () => {
    worker.use(http.get("/api/me/quota", () => new HttpResponse(null, { status: 500 })));

    await expect(getQuota()).rejects.toThrow(/500/);
  });
});
