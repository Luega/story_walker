import { render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { worker } from "@/test/handlers";
import { aConversation, aConversationSummary, aStorySummary } from "@/test/factories";
import DashboardPage from "./page";

/** Serves the list and counts the requests, so polling behaviour is observable. */
function serveStories(...stories: ReturnType<typeof aStorySummary>[]) {
  const counter = { calls: 0 };
  worker.use(
    http.get("/api/stories", () => {
      counter.calls += 1;
      return HttpResponse.json(stories);
    }),
  );
  return counter;
}

describe("listing", () => {
  it("shows the stories the API returns", async () => {
    serveStories(aStorySummary({ title: "The Curious Moon" }), aStorySummary({ title: "The Woods" }));
    render(<DashboardPage />);

    expect(await screen.findByRole("link", { name: "The Curious Moon" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "The Woods" })).toBeInTheDocument();
  });

  it("invites the user to start when there are no stories", async () => {
    serveStories();
    render(<DashboardPage />);

    expect(await screen.findByText(/No stories yet/)).toBeInTheDocument();
  });

  it("reports a failure to load", async () => {
    worker.use(http.get("/api/stories", () => new HttpResponse(null, { status: 500 })));
    render(<DashboardPage />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/500/);
  });

  it("opens the new-story modal", async () => {
    serveStories();
    render(<DashboardPage />);
    await screen.findByText(/No stories yet/);

    await userEvent.click(screen.getByRole("button", { name: "+ New story" }));

    expect(screen.getByRole("heading", { name: "New story" })).toBeInTheDocument();
  });
});

// The conversation history (ADR-0028).
describe("conversations", () => {
  it("opens the history, then the chosen conversation in the chat", async () => {
    serveStories();
    const summary = aConversationSummary({ title: "George and the Paper Ears" });
    const conversation = aConversation({
      id: summary.id,
      title: summary.title,
      messages: [{ role: "assistant", text: "Shall we change the scene with the scissors?" }],
    });
    worker.use(
      http.get("/api/stories/conversations", () => HttpResponse.json([summary])),
      http.get(`/api/stories/conversations/${summary.id}`, () => HttpResponse.json(conversation)),
    );
    render(<DashboardPage />);
    await screen.findByText(/No stories yet/);

    await userEvent.click(screen.getByRole("button", { name: "Conversations" }));
    await userEvent.click(await screen.findByRole("button", { name: "George and the Paper Ears" }));

    expect(
      await screen.findByText("Shall we change the scene with the scissors?"),
    ).toBeInTheDocument();
    // The history made way for the chat.
    expect(screen.queryByRole("button", { name: "Close conversations" })).not.toBeInTheDocument();
  });
});

// From a finished conversation to a new story built on it (ADR-0029).
describe("a related story", () => {
  it("starts from a completed conversation in the history", async () => {
    serveStories();
    const summary = aConversationSummary({
      title: "The Fox and the Moon",
      status: "Completed",
      resumable: false,
      latestCompleteStoryId: "story-7",
    });
    worker.use(
      http.get("/api/stories/conversations", () => HttpResponse.json([summary])),
      http.get(`/api/stories/conversations/${summary.id}`, () =>
        HttpResponse.json(
          aConversation({
            id: summary.id,
            title: summary.title,
            status: "Completed",
            resumable: false,
            latestCompleteStoryId: "story-7",
          }),
        ),
      ),
    );
    render(<DashboardPage />);
    await screen.findByText(/No stories yet/);

    await userEvent.click(screen.getByRole("button", { name: "Conversations" }));
    await userEvent.click(await screen.findByRole("tab", { name: "Completed" }));
    await userEvent.click(screen.getByRole("button", { name: "The Fox and the Moon" }));
    await userEvent.click(await screen.findByRole("button", { name: "New story based on this one" }));

    expect(
      await screen.findByRole("heading", { name: "New story based on “The Fox and the Moon”" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("textbox")).toBeInTheDocument();
  });
});

// The weekly limit (ADR-0030).
describe("the weekly limit", () => {
  function serveQuota(remaining: number, resetsAt = "2026-09-28T00:00:00Z") {
    worker.use(
      http.get("/api/me/quota", () =>
        HttpResponse.json({ limit: 7, used: 7 - remaining, remaining, resetsAt }),
      ),
    );
  }

  it("says how many stories are left this week", async () => {
    serveStories();
    serveQuota(5);
    render(<DashboardPage />);

    expect(await screen.findByText("5 of 7 stories left this week")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "+ New story" })).toBeEnabled();
  });

  it("at the limit, says when more are possible, in local time, and offers no new story", async () => {
    serveStories();
    serveQuota(0, "2026-09-28T00:00:00Z");
    render(<DashboardPage />);

    // Shown in the browser's own time zone: 02:00 on Monday in Italy in September.
    const when = new Intl.DateTimeFormat("en-GB", {
      weekday: "long",
      day: "numeric",
      month: "long",
      hour: "2-digit",
      minute: "2-digit",
    }).format(new Date("2026-09-28T00:00:00Z"));
    expect(await screen.findByText(new RegExp(`You've used this week's 7 stories`))).toHaveTextContent(
      when,
    );
    expect(screen.getByRole("button", { name: "+ New story" })).toBeDisabled();
  });
});

describe("deleting a story", () => {
  it("asks for confirmation, then removes the card without reloading the list", async () => {
    const story = aStorySummary({ title: "To delete" });
    const list = serveStories(story);
    let deleted: string | null = null;
    worker.use(
      http.delete("/api/stories/:id", ({ params }) => {
        deleted = String(params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "To delete" });

    await userEvent.click(screen.getByRole("button", { name: "Delete “To delete”" }));
    await userEvent.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(screen.queryByText("To delete")).not.toBeInTheDocument());
    expect(deleted).toBe(story.id);
    // The list is dropped locally; refetching would be a wasted round trip.
    expect(list.calls).toBe(1);
  });

  it("keeps the story and explains why when the delete fails", async () => {
    serveStories(aStorySummary({ title: "Stubborn" }));
    worker.use(http.delete("/api/stories/:id", () => new HttpResponse(null, { status: 500 })));
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Stubborn" });

    await userEvent.click(screen.getByRole("button", { name: "Delete “Stubborn”" }));
    await userEvent.click(screen.getByRole("button", { name: "Delete" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/500/);
    expect(screen.getByRole("link", { name: "Stubborn" })).toBeInTheDocument();
  });

  it("leaves the story alone when the confirmation is dismissed", async () => {
    serveStories(aStorySummary({ title: "Kept" }));
    let deleteCalls = 0;
    worker.use(
      http.delete("/api/stories/:id", () => {
        deleteCalls += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Kept" });

    await userEvent.click(screen.getByRole("button", { name: "Delete “Kept”" }));
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(deleteCalls).toBe(0);
    expect(screen.getByRole("link", { name: "Kept" })).toBeInTheDocument();
  });
});

describe("renaming a story", () => {
  it("sends one PATCH and shows the title the server stored", async () => {
    const story = aStorySummary({ title: "Old title" });
    serveStories(story);
    const patches: unknown[] = [];
    worker.use(
      http.patch("/api/stories/:id", async ({ request }) => {
        patches.push(await request.json());
        // The server trims; the dashboard must display this, not the raw input.
        return HttpResponse.json({
          ...story,
          title: "New title",
          targetAge: 5,
          artStyle: "watercolour",
          pages: [],
        });
      }),
    );
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Old title" });

    await userEvent.click(screen.getByRole("button", { name: "Rename “Old title”" }));
    const input = screen.getByRole("textbox", { name: "Story title" });
    await userEvent.fill(input, "  New title  ");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("link", { name: "New title" })).toBeInTheDocument();
    // One request, not two: the blur fired by disabling the input must not send a second PATCH.
    expect(patches).toEqual([{ title: "New title" }]);
  });

  it("reports a failed rename and keeps the edit open", async () => {
    serveStories(aStorySummary({ title: "Old title" }));
    worker.use(http.patch("/api/stories/:id", () => new HttpResponse(null, { status: 500 })));
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Old title" });

    await userEvent.click(screen.getByRole("button", { name: "Rename “Old title”" }));
    const input = screen.getByRole("textbox", { name: "Story title" });
    await userEvent.fill(input, "New title");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("alert")).toHaveTextContent(/500/);
    expect(screen.getByRole("textbox", { name: "Story title" })).toHaveValue("New title");
  });
});

describe("when the agent finishes the chat", () => {
  /** The agent queues a story; the list serves it only from the next fetch onwards. */
  function agentQueues(story: ReturnType<typeof aStorySummary>) {
    let list: ReturnType<typeof aStorySummary>[] = [];
    const counter = { calls: 0 };
    worker.use(
      http.get("/api/stories", () => {
        counter.calls += 1;
        return HttpResponse.json(list);
      }),
      http.post("/api/stories/conversations", () => {
        list = [story];
        return HttpResponse.json({ storyId: story.id, status: "Generating" }, { status: 202 });
      }),
    );
    return counter;
  }

  /** Opens the chat and sends the one turn that makes the agent author the brief. */
  async function chatUntilQueued() {
    await userEvent.click(screen.getByRole("button", { name: "+ New story" }));
    await userEvent.fill(screen.getByRole("textbox"), "yes, confirm");
    await userEvent.keyboard("{Enter}");
  }

  it("closes the chat and shows the queued story without leaving the dashboard", async () => {
    const list = agentQueues(aStorySummary({ status: "Generating", title: "The Kind Dragon" }));
    render(<DashboardPage />);
    await screen.findByText(/No stories yet/);

    await chatUntilQueued();

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "New story" })).not.toBeInTheDocument(),
    );
    // Refetched straight away rather than left stale until the next poll. Not a link: it is still
    // generating, so the card shows the title without a way in.
    expect(await screen.findByRole("heading", { name: "The Kind Dragon" })).toBeInTheDocument();
    expect(list.calls).toBeGreaterThan(1);
  });

  it("tells the user the story is being generated", async () => {
    agentQueues(aStorySummary({ status: "Generating", title: "The Kind Dragon" }));
    render(<DashboardPage />);
    await screen.findByText(/No stories yet/);

    await chatUntilQueued();

    expect(await screen.findByRole("status")).toHaveTextContent(/being generated/i);
  });

  it("lets the user dismiss the notice", async () => {
    agentQueues(aStorySummary({ status: "Generating", title: "The Kind Dragon" }));
    render(<DashboardPage />);
    await screen.findByText(/No stories yet/);
    await chatUntilQueued();
    await screen.findByRole("status");

    await userEvent.click(screen.getByRole("button", { name: "Dismiss" }));

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("drops the notice once the story is no longer generating", async () => {
    agentQueues(aStorySummary({ status: "Complete", title: "The Kind Dragon" }));
    render(<DashboardPage />);
    await screen.findByText(/No stories yet/);

    await chatUntilQueued();

    // The refetch already says Complete, so there is nothing left for the user to wait for.
    expect(await screen.findByRole("link", { name: "The Kind Dragon" })).toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});

describe("polling", () => {
  // POLL_MS is 5s in page.tsx, so these two tests wait in real time. They are the slowest in the
  // suite; the alternative (faking timers) also fakes the timing of the fetches they depend on.
  it("keeps refreshing while a story is still generating", async () => {
    const list = serveStories(aStorySummary({ status: "Generating", title: "In corso" }));
    render(<DashboardPage />);
    await screen.findByRole("heading", { name: "In corso" });

    await waitFor(() => expect(list.calls).toBeGreaterThan(1), { timeout: 8000 });
  }, 10_000);

  it("stops refreshing once nothing is generating", async () => {
    const list = serveStories(aStorySummary({ status: "Complete", title: "Finished" }));
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Finished" });

    await new Promise((r) => setTimeout(r, 6000));

    expect(list.calls).toBe(1);
  }, 10_000);
});
