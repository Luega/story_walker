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

    expect(screen.getByText(/Raccontami che storia vorresti/)).toBeInTheDocument();
  });

  it("shows the user's message and the agent's reply", async () => {
    agentReplies("Che bella idea!");
    const { input } = renderModal();

    await userEvent.fill(input, "un drago gentile");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByText("Che bella idea!")).toBeInTheDocument();
    expect(screen.getByText("un drago gentile")).toBeInTheDocument();
  });

  it("carries the conversation id into the next turn", async () => {
    const turns = agentReplies("Continua pure", "conv-42");
    const { input } = renderModal();

    await userEvent.fill(input, "primo");
    await userEvent.keyboard("{Enter}");
    await screen.findByText("Continua pure");

    await userEvent.fill(screen.getByRole("textbox"), "secondo");
    await userEvent.keyboard("{Enter}");

    await vi.waitFor(() => expect(turns).toHaveLength(2));
    expect(turns[0].conversationId).toBeNull();
    expect(turns[1].conversationId).toBe("conv-42");
  });

  it("treats Shift+Enter as a newline rather than a send", async () => {
    const turns = agentReplies("mai chiamato");
    const { input } = renderModal();

    await userEvent.fill(input, "prima riga");
    await userEvent.keyboard("{Shift>}{Enter}{/Shift}");

    expect(turns).toHaveLength(0);
    expect(screen.getByRole("textbox")).toHaveValue("prima riga\n");
  });

  it("cannot be sent while the message is blank", async () => {
    agentReplies("mai chiamato");
    const { input } = renderModal();
    const send = () => screen.getByRole("button", { name: "Invia" });

    expect(send()).toBeDisabled();

    await userEvent.fill(input, "   ");
    expect(send()).toBeDisabled();

    await userEvent.fill(input, "un drago");
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

    await userEvent.fill(input, "sì, confermo");
    await userEvent.keyboard("{Enter}");

    await vi.waitFor(() => expect(push).toHaveBeenCalledWith("/stories/story-7"));
  });
});

describe("when something goes wrong", () => {
  it("starts over when the conversation has expired", async () => {
    worker.use(http.post(CONVERSATIONS, () => new HttpResponse(null, { status: 404 })));
    const { input } = renderModal();

    await userEvent.fill(input, "ci sei?");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("alert")).toHaveTextContent(/ricominciamo da capo/);
    // The transcript is reset to the greeting, so the user is not replying into a dead conversation.
    expect(screen.getByText(/Raccontami che storia vorresti/)).toBeInTheDocument();
    expect(screen.queryByText("ci sei?")).not.toBeInTheDocument();
  });

  it("gives the typed message back so it is not lost", async () => {
    worker.use(
      http.post(CONVERSATIONS, () =>
        HttpResponse.json({ detail: "Brief non valido." }, { status: 502 }),
      ),
    );
    const { input } = renderModal();

    await userEvent.fill(input, "un drago gentile");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("alert")).toHaveTextContent("Brief non valido.");
    expect(screen.getByRole("textbox")).toHaveValue("un drago gentile");
  });
});

describe("closing", () => {
  it("closes from the header button", async () => {
    const { onClose } = renderModal();

    await userEvent.click(screen.getByRole("button", { name: "Chiudi" }));

    expect(onClose).toHaveBeenCalledOnce();
  });

  it("closes on Escape", async () => {
    const { onClose } = renderModal();

    await userEvent.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalledOnce();
  });
});
