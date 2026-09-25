import { render, screen, within } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { worker } from "@/test/handlers";
import { aConversationSummary } from "@/test/factories";
import type { ConversationSummary } from "@/lib/api";
import { ConversationSidebar } from "./ConversationSidebar";

const CONVERSATIONS = "/api/stories/conversations";

function serveConversations(...conversations: ConversationSummary[]) {
  worker.use(http.get(CONVERSATIONS, () => HttpResponse.json(conversations)));
}

function renderSidebar() {
  const onOpen = vi.fn();
  const onClose = vi.fn();
  render(<ConversationSidebar onOpen={onOpen} onClose={onClose} />);
  return { onOpen, onClose };
}

/** The list item holding a conversation's title button. */
const rowOf = (title: string) => screen.getByRole("button", { name: title }).closest("li")!;

describe("listing", () => {
  it("shows the conversations in the order the API sends them, most recent first", async () => {
    serveConversations(
      aConversationSummary({ title: "Newest" }),
      aConversationSummary({ title: "Older" }),
    );
    renderSidebar();

    await screen.findByRole("button", { name: "Newest" });
    const titles = within(screen.getByRole("list"))
      .getAllByRole("listitem")
      .map((li) => li.querySelector("button")!.textContent);
    expect(titles).toEqual(["Newest", "Older"]);
  });

  it("says where each conversation stands", async () => {
    serveConversations(
      aConversationSummary({ title: "Going on", resumable: true, storyFailed: false }),
      aConversationSummary({ title: "Gone wrong", resumable: true, storyFailed: true }),
      aConversationSummary({ title: "All done", status: "Completed", resumable: false }),
    );
    renderSidebar();

    await screen.findByRole("button", { name: "Going on" });
    expect(rowOf("Going on")).toHaveTextContent("In progress");
    expect(rowOf("Gone wrong")).toHaveTextContent(/Story failed/);
    expect(rowOf("All done")).toHaveTextContent("Story created");
  });

  it("says when there is nothing yet", async () => {
    serveConversations();
    renderSidebar();

    expect(await screen.findByText(/No conversations yet/)).toBeInTheDocument();
  });

  it("reports a failure to load", async () => {
    worker.use(http.get(CONVERSATIONS, () => new HttpResponse(null, { status: 500 })));
    renderSidebar();

    expect(await screen.findByRole("alert")).toHaveTextContent(/500/);
  });
});

describe("opening", () => {
  it("hands the chosen conversation to the dashboard", async () => {
    const conversation = aConversationSummary({ title: "Bedtime ideas" });
    serveConversations(conversation);
    const { onOpen } = renderSidebar();

    await userEvent.click(await screen.findByRole("button", { name: "Bedtime ideas" }));

    expect(onOpen).toHaveBeenCalledExactlyOnceWith(conversation.id);
  });
});

describe("renaming", () => {
  it("saves the new title and shows the one the server stored", async () => {
    const conversation = aConversationSummary({ title: "Old title" });
    serveConversations(conversation);
    let body: unknown;
    worker.use(
      http.patch(`${CONVERSATIONS}/${conversation.id}`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ...conversation, title: "New title" });
      }),
    );
    renderSidebar();

    await userEvent.click(await screen.findByRole("button", { name: "Rename “Old title”" }));
    const input = screen.getByRole("textbox", { name: "Conversation title" });
    await userEvent.fill(input, "  New title  ");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("button", { name: "New title" })).toBeInTheDocument();
    expect(body).toEqual({ title: "New title" });
  });
});

describe("deleting", () => {
  it("asks for confirmation, saying the stories are kept, then removes the conversation", async () => {
    const conversation = aConversationSummary({ title: "Bedtime ideas" });
    serveConversations(conversation);
    let deleted: string | undefined;
    worker.use(
      http.delete(`${CONVERSATIONS}/:id`, ({ params }) => {
        deleted = params.id as string;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderSidebar();

    await userEvent.click(await screen.findByRole("button", { name: "Delete “Bedtime ideas”" }));
    expect(screen.getByText(/Stories created from it are kept/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Delete" }));

    await expect.poll(() => screen.queryByRole("button", { name: "Bedtime ideas" })).toBeNull();
    expect(deleted).toBe(conversation.id);
  });
});

describe("closing", () => {
  it("closes from the header button", async () => {
    serveConversations();
    const { onClose } = renderSidebar();

    await userEvent.click(screen.getByRole("button", { name: "Close conversations" }));

    expect(onClose).toHaveBeenCalledOnce();
  });
});
