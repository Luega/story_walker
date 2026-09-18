import { render, screen } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { worker } from "@/test/handlers";
import { NewStoryModal } from "./NewStoryModal";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

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
  render(<NewStoryModal onClose={onClose} />);
  return { onClose, input: screen.getByRole("textbox") };
}

beforeEach(() => push.mockClear());

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
  it("navigates to the queued story", async () => {
    worker.use(
      http.post(CONVERSATIONS, () =>
        HttpResponse.json({ storyId: "story-7", status: "Generating" }, { status: 202 }),
      ),
    );
    const { input } = renderModal();

    await userEvent.fill(input, "yes, confirm");
    await userEvent.keyboard("{Enter}");

    await vi.waitFor(() => expect(push).toHaveBeenCalledWith("/stories/story-7"));
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
