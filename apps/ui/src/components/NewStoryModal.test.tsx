import { render, screen } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { worker } from "@/test/handlers";
import { aConversation } from "@/test/factories";
import { NewStoryModal } from "./NewStoryModal";

const CONVERSATIONS = "/api/stories/conversations";

/** Replies with a fixed message and records every turn the UI sends. */
function agentReplies(reply: string, conversationId = "conv-1") {
  const turns: { conversationId: string | null; message: string }[] = [];
  worker.use(
    http.post(CONVERSATIONS, async ({ request }) => {
      turns.push((await request.json()) as (typeof turns)[number]);
      return HttpResponse.json({ conversationId, reply, briefReady: false });
    }),
  );
  return turns;
}

function renderModal() {
  const onClose = vi.fn();
  const onQueued = vi.fn();
  render(<NewStoryModal onClose={onClose} onQueued={onQueued} />);
  return { onClose, onQueued, input: screen.getByRole("textbox") };
}

describe("the conversation", () => {
  it("greets the user with a prompt about what to write", () => {
    renderModal();

    expect(screen.getByText(/Tell me about the story you'd like/)).toBeInTheDocument();
  });

  it("shows the user's message and the agent's reply", async () => {
    agentReplies("What a lovely idea!");
    const { input } = renderModal();

    await userEvent.fill(input, "a kind dragon");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByText("What a lovely idea!")).toBeInTheDocument();
    expect(screen.getByText("a kind dragon")).toBeInTheDocument();
  });

  it("carries the conversation id into the next turn", async () => {
    const turns = agentReplies("Go on", "conv-42");
    const { input } = renderModal();

    await userEvent.fill(input, "first");
    await userEvent.keyboard("{Enter}");
    await screen.findByText("Go on");

    await userEvent.fill(screen.getByRole("textbox"), "second");
    await userEvent.keyboard("{Enter}");

    await vi.waitFor(() => expect(turns).toHaveLength(2));
    expect(turns[0].conversationId).toBeNull();
    expect(turns[1].conversationId).toBe("conv-42");
  });

  it("treats Shift+Enter as a newline rather than a send", async () => {
    const turns = agentReplies("never called");
    const { input } = renderModal();

    await userEvent.fill(input, "first line");
    await userEvent.keyboard("{Shift>}{Enter}{/Shift}");

    expect(turns).toHaveLength(0);
    expect(screen.getByRole("textbox")).toHaveValue("first line\n");
  });

  it("cannot be sent while the message is blank", async () => {
    agentReplies("never called");
    const { input } = renderModal();
    const send = () => screen.getByRole("button", { name: "Send" });

    expect(send()).toBeDisabled();

    await userEvent.fill(input, "   ");
    expect(send()).toBeDisabled();

    await userEvent.fill(input, "a dragon");
    expect(send()).toBeEnabled();
  });
});

describe("when the brief is ready", () => {
  it("hands the queued story to the dashboard and closes", async () => {
    worker.use(
      http.post(CONVERSATIONS, () =>
        HttpResponse.json({ storyId: "story-7", status: "Generating" }, { status: 202 }),
      ),
    );
    const { input, onQueued, onClose } = renderModal();

    await userEvent.fill(input, "yes, confirm");
    await userEvent.keyboard("{Enter}");

    await vi.waitFor(() => expect(onQueued).toHaveBeenCalledWith("story-7"));
    // The user lands back on the dashboard, so the modal dismisses itself once the brief is in.
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce());
  });
});

describe("when something goes wrong", () => {
  it("starts over when the conversation has expired", async () => {
    worker.use(http.post(CONVERSATIONS, () => new HttpResponse(null, { status: 404 })));
    const { input } = renderModal();

    await userEvent.fill(input, "are you there?");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("alert")).toHaveTextContent(/start over/);
    // The transcript is reset to the greeting, so the user is not replying into a dead conversation.
    expect(screen.getByText(/Tell me about the story you'd like/)).toBeInTheDocument();
    expect(screen.queryByText("are you there?")).not.toBeInTheDocument();
  });

  it("gives the typed message back so it is not lost", async () => {
    worker.use(
      http.post(CONVERSATIONS, () =>
        HttpResponse.json({ detail: "Invalid brief." }, { status: 502 }),
      ),
    );
    const { input } = renderModal();

    await userEvent.fill(input, "a kind dragon");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("alert")).toHaveTextContent("Invalid brief.");
    expect(screen.getByRole("textbox")).toHaveValue("a kind dragon");
  });
});

describe("closing", () => {
  it("closes from the header button", async () => {
    const { onClose } = renderModal();

    await userEvent.click(screen.getByRole("button", { name: "Close" }));

    expect(onClose).toHaveBeenCalledOnce();
  });

  it("closes on Escape", async () => {
    const { onClose } = renderModal();

    await userEvent.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalledOnce();
  });
});

describe("the saved story settings", () => {
  it("says the agent already has them, and links to where they are edited", () => {
    renderModal();

    expect(screen.getByRole("link", { name: /story settings/ })).toHaveAttribute(
      "href",
      "/settings",
    );
  });
});

// Conversations are kept, and our staff can see them: the parent is told before they type (ADR-0028).
describe("the privacy notice", () => {
  it("says the conversation is saved and who may see it, and links to the privacy page", () => {
    renderModal();

    expect(screen.getByText(/Conversations are saved/)).toHaveTextContent(/technical staff/);
    expect(screen.getByRole("link", { name: "Privacy" })).toHaveAttribute("href", "/privacy");
  });
});

// Opened from the conversation history (ADR-0028).
describe("an existing conversation", () => {
  function renderExisting(conversation = aConversation()) {
    worker.use(
      http.get(`${CONVERSATIONS}/${conversation.id}`, () => HttpResponse.json(conversation)),
    );
    render(
      <NewStoryModal conversationId={conversation.id} onClose={vi.fn()} onQueued={vi.fn()} />,
    );
    return conversation;
  }

  it("shows what was said instead of the greeting, under the conversation's title", async () => {
    renderExisting(
      aConversation({
        title: "George and the Paper Ears",
        messages: [
          { role: "user", text: "A story about George" },
          { role: "assistant", text: "The picture could not be made. Shall we change the scene?" },
        ],
      }),
    );

    expect(await screen.findByText("A story about George")).toBeInTheDocument();
    expect(
      screen.getByText("The picture could not be made. Shall we change the scene?"),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Tell me about the story you'd like/)).not.toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "George and the Paper Ears" }),
    ).toBeInTheDocument();
  });

  it("continues that conversation rather than starting a new one", async () => {
    const conversation = renderExisting();
    const turns = agentReplies("Great, let's change it", conversation.id);

    await userEvent.fill(await screen.findByRole("textbox"), "Yes, put the scissors down");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByText("Great, let's change it")).toBeInTheDocument();
    expect(turns).toEqual([{ conversationId: conversation.id, message: "Yes, put the scissors down" }]);
  });

  it("can only be read once its story has been created", async () => {
    renderExisting(aConversation({ status: "Completed", resumable: false }));

    expect(await screen.findByText(/already been created/)).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("says so when the conversation no longer exists", async () => {
    worker.use(
      http.get(`${CONVERSATIONS}/:id`, () => new HttpResponse(null, { status: 404 })),
    );
    render(<NewStoryModal conversationId="gone" onClose={vi.fn()} onQueued={vi.fn()} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/no longer available/);
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });
});
