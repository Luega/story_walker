import { render, screen } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { worker } from "@/test/handlers";
import { aPage, aStory } from "@/test/factories";
import StoryPage from "./page";

const STORY_ID = "11111111-1111-4111-8111-111111111111";

// The page reads its id from the route; only that is faked.
vi.mock("next/navigation", () => ({ useParams: () => ({ id: STORY_ID }) }));

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

describe("status messages", () => {
  it("says the story is still being generated", async () => {
    serveStory({ status: "Generating" });
    render(<StoryPage />);

    expect(await screen.findByText(/pages will appear here/)).toBeInTheDocument();
  });

  it("says when generation failed", async () => {
    serveStory({ status: "Failed" });
    render(<StoryPage />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be generated/);
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
