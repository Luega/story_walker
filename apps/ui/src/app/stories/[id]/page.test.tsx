import { render, screen } from "@testing-library/react";
import { page, userEvent } from "vitest/browser";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { worker } from "@/test/handlers";
import { aPage, aStory } from "@/test/factories";
import StoryPage from "./page";

const STORY_ID = "11111111-1111-4111-8111-111111111111";

// The page reads its id from the route and sends unreadable stories away; only those are faked.
const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useParams: () => ({ id: STORY_ID }),
  useRouter: () => ({ replace }),
}));

beforeEach(() => replace.mockClear());

function serveStory(overrides: Parameters<typeof aStory>[0] = {}) {
  const story = aStory({ id: STORY_ID, ...overrides });
  worker.use(http.get(`/api/stories/${STORY_ID}`, () => HttpResponse.json(story)));
  return story;
}

const sixPages = Array.from({ length: 6 }, (_, i) =>
  aPage({ pageNo: i + 1, content: `Content of page ${i + 1}` }),
);

describe("loading the story", () => {
  it("shows the story once it arrives", async () => {
    serveStory({ title: "The Kind Dragon", targetAge: 7, artStyle: "watercolour" });
    render(<StoryPage />);

    expect(await screen.findByRole("heading", { name: "The Kind Dragon" })).toBeInTheDocument();
    expect(screen.getByText("Age 7+")).toBeInTheDocument();
    expect(screen.getByText("watercolour")).toBeInTheDocument();
  });

  it("offers a way back when the story does not exist", async () => {
    worker.use(http.get(`/api/stories/${STORY_ID}`, () => new HttpResponse(null, { status: 404 })));
    render(<StoryPage />);

    expect(await screen.findByText("Story not found.")).toBeInTheDocument();
    // The stories list moved to /dashboard when / became the public landing page.
    expect(screen.getByRole("link", { name: /Back to your stories/ })).toHaveAttribute(
      "href",
      "/dashboard",
    );
  });

  it("reports a server failure", async () => {
    worker.use(http.get(`/api/stories/${STORY_ID}`, () => new HttpResponse(null, { status: 500 })));
    render(<StoryPage />);

    expect(await screen.findByText(/500/)).toBeInTheDocument();
  });
});

describe("stories that cannot be read yet", () => {
  it.each(["Draft", "Generating", "Failed"] as const)(
    "sends the reader back to the dashboard when the story is %s",
    async (status) => {
      serveStory({ status, title: "Not ready" });
      render(<StoryPage />);

      await vi.waitFor(() => expect(replace).toHaveBeenCalledWith("/dashboard"));
      // Nothing of the story is shown on the way out.
      expect(screen.queryByRole("heading", { name: "Not ready" })).not.toBeInTheDocument();
    },
  );

  it("opens a story that is ready instead of redirecting", async () => {
    serveStory({ status: "Complete", title: "The Kind Dragon" });
    render(<StoryPage />);

    expect(await screen.findByRole("heading", { name: "The Kind Dragon" })).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });
});

describe("turning pages", () => {
  it("shows two facing pages at a time", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);

    expect(await screen.findByText("Content of page 1")).toBeInTheDocument();
    expect(screen.getByText("Content of page 2")).toBeInTheDocument();
    expect(screen.queryByText("Content of page 3")).not.toBeInTheDocument();
    expect(screen.getByText(/Pages 1–2 of 6/)).toBeInTheDocument();
  });

  it("moves forward and back with the buttons", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Content of page 1");

    await userEvent.click(screen.getByRole("button", { name: /Next/ }));
    expect(screen.getByText("Content of page 3")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Previous/ }));
    expect(screen.getByText("Content of page 1")).toBeInTheDocument();
  });

  it("moves with the arrow keys", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Content of page 1");

    await userEvent.keyboard("{ArrowRight}");
    expect(screen.getByText("Content of page 3")).toBeInTheDocument();

    await userEvent.keyboard("{ArrowLeft}");
    expect(screen.getByText("Content of page 1")).toBeInTheDocument();
  });

  it("stops at the first and last spread", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Content of page 1");

    expect(screen.getByRole("button", { name: /Previous/ })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: /Next/ }));
    await userEvent.click(screen.getByRole("button", { name: /Next/ }));

    expect(screen.getByText(/Pages 5–6 of 6/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Next/ })).toBeDisabled();
  });

  it("labels a lone trailing page in the singular", async () => {
    serveStory({ pages: [aPage({ pageNo: 1 }), aPage({ pageNo: 2 }), aPage({ pageNo: 3 })] });
    render(<StoryPage />);
    await screen.findByText(/Pages 1–2 of 3/);

    await userEvent.click(screen.getByRole("button", { name: /Next/ }));

    expect(screen.getByText(/Page 3 of 3/)).toBeInTheDocument();
  });
});

describe("illustrations", () => {
  it("shows the illustration with a description of which page it belongs to", async () => {
    serveStory({ pages: [aPage({ pageNo: 1, imageUrl: "https://example.test/1.png" })] });
    render(<StoryPage />);

    expect(await screen.findByRole("img", { name: "Illustration for page 1" })).toHaveAttribute(
      "src",
      "https://example.test/1.png",
    );
  });

  it("says an illustration is still coming while the page is pending", async () => {
    serveStory({ pages: [aPage({ pageNo: 1, imageUrl: null, status: "Pending" })] });
    render(<StoryPage />);

    expect(await screen.findByText(/Illustration on its way/)).toBeInTheDocument();
  });

  it("says an illustration is unavailable when its page failed", async () => {
    serveStory({ pages: [aPage({ pageNo: 1, imageUrl: null, status: "Failed" })] });
    render(<StoryPage />);

    expect(await screen.findByText(/Illustration unavailable/)).toBeInTheDocument();
  });
});

describe("reading on a tablet", () => {
  // A tablet gets the phone's reader, one page at a time, not a spread (ADR-0033).
  beforeEach(() => page.viewport(820, 1180));
  afterEach(() => page.viewport(1280, 800));

  it("shows one page at a time", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);

    expect(await screen.findByText("Content of page 1")).toBeInTheDocument();
    expect(screen.queryByText("Content of page 2")).not.toBeInTheDocument();
    expect(screen.getByText(/Page 1 of 6/)).toBeInTheDocument();
  });

  it("turns one page at a time", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Content of page 1");

    await userEvent.click(screen.getByRole("button", { name: "Next page" }));

    expect(screen.getByText("Content of page 2")).toBeInTheDocument();
  });
});

describe("reading on a phone", () => {
  // The suite pins a desktop viewport; the immersive reader only exists below it.
  beforeEach(() => page.viewport(390, 844));
  afterEach(() => page.viewport(1280, 800));

  /** A flick across the reader: pointer down, moved by `dx`, up. A tap is a swipe of zero. */
  async function swipe(dx: number) {
    const reader = screen.getByRole("region", { name: "Story reader" });
    const from = 200;
    for (const [type, clientX] of [
      ["pointerdown", from],
      ["pointerup", from + dx],
    ] as const) {
      reader.dispatchEvent(
        new PointerEvent(type, { bubbles: true, clientX, pointerId: 1, isPrimary: true }),
      );
    }
  }

  it("shows one page at a time", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);

    expect(await screen.findByText("Content of page 1")).toBeInTheDocument();
    expect(screen.queryByText("Content of page 2")).not.toBeInTheDocument();
    expect(screen.getByText(/Page 1 of 6/)).toBeInTheDocument();
  });

  it("shows the story alone, without the details that belong to the dashboard", async () => {
    serveStory({ pages: sixPages, targetAge: 7, artStyle: "watercolour" });
    render(<StoryPage />);
    await screen.findByText("Content of page 1");

    expect(screen.queryByText("Age 7+")).not.toBeInTheDocument();
    expect(screen.queryByText("watercolour")).not.toBeInTheDocument();
  });

  it("turns one page at a time with the reader controls", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Content of page 1");

    await userEvent.click(screen.getByRole("button", { name: "Next page" }));

    expect(screen.getByText("Content of page 2")).toBeInTheDocument();
    expect(screen.getByText(/Page 2 of 6/)).toBeInTheDocument();
  });

  it("turns the page when the reader is swiped", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Content of page 1");

    await swipe(-120);
    expect(screen.getByText("Content of page 2")).toBeInTheDocument();

    await swipe(120);
    expect(screen.getByText("Content of page 1")).toBeInTheDocument();
  });

  it("keeps the controls in view while reading", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Content of page 1");

    // A tap on the page is not a page turn, and nothing hides the buttons afterwards.
    await swipe(0);
    await new Promise((resolve) => setTimeout(resolve, 3500));

    expect(screen.getByText("Content of page 1")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next page" })).toBeVisible();
  });
});
