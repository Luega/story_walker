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
  aPage({ pageNo: i + 1, content: `Contenuto della pagina ${i + 1}` }),
);

describe("loading the story", () => {
  it("shows the story once it arrives", async () => {
    serveStory({ title: "Il drago gentile", targetAge: 7, artStyle: "acquerello" });
    render(<StoryPage />);

    expect(await screen.findByRole("heading", { name: "Il drago gentile" })).toBeInTheDocument();
    expect(screen.getByText("Età 7+")).toBeInTheDocument();
    expect(screen.getByText("acquerello")).toBeInTheDocument();
  });

  it("offers a way back when the story does not exist", async () => {
    worker.use(http.get(`/api/stories/${STORY_ID}`, () => new HttpResponse(null, { status: 404 })));
    render(<StoryPage />);

    expect(await screen.findByText("Storia non trovata.")).toBeInTheDocument();
    // The stories list moved to /dashboard when / became the public landing page.
    expect(screen.getByRole("link", { name: /Torna alle storie/ })).toHaveAttribute(
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

    expect(await screen.findByText(/Generazione in corso/)).toBeInTheDocument();
  });

  it("says when generation failed", async () => {
    serveStory({ status: "Failed" });
    render(<StoryPage />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/non è andata a buon fine/);
  });
});

describe("turning pages", () => {
  it("shows two facing pages at a time", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);

    expect(await screen.findByText("Contenuto della pagina 1")).toBeInTheDocument();
    expect(screen.getByText("Contenuto della pagina 2")).toBeInTheDocument();
    expect(screen.queryByText("Contenuto della pagina 3")).not.toBeInTheDocument();
    expect(screen.getByText(/Pagine 1–2 di 6/)).toBeInTheDocument();
  });

  it("moves forward and back with the buttons", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Contenuto della pagina 1");

    await userEvent.click(screen.getByRole("button", { name: /Successiva/ }));
    expect(screen.getByText("Contenuto della pagina 3")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Precedente/ }));
    expect(screen.getByText("Contenuto della pagina 1")).toBeInTheDocument();
  });

  it("moves with the arrow keys", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Contenuto della pagina 1");

    await userEvent.keyboard("{ArrowRight}");
    expect(screen.getByText("Contenuto della pagina 3")).toBeInTheDocument();

    await userEvent.keyboard("{ArrowLeft}");
    expect(screen.getByText("Contenuto della pagina 1")).toBeInTheDocument();
  });

  it("stops at the first and last spread", async () => {
    serveStory({ pages: sixPages });
    render(<StoryPage />);
    await screen.findByText("Contenuto della pagina 1");

    expect(screen.getByRole("button", { name: /Precedente/ })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: /Successiva/ }));
    await userEvent.click(screen.getByRole("button", { name: /Successiva/ }));

    expect(screen.getByText(/Pagine 5–6 di 6/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Successiva/ })).toBeDisabled();
  });

  it("labels a lone trailing page in the singular", async () => {
    serveStory({ pages: [aPage({ pageNo: 1 }), aPage({ pageNo: 2 }), aPage({ pageNo: 3 })] });
    render(<StoryPage />);
    await screen.findByText(/Pagine 1–2 di 3/);

    await userEvent.click(screen.getByRole("button", { name: /Successiva/ }));

    expect(screen.getByText(/Pagina 3 di 3/)).toBeInTheDocument();
  });
});

describe("illustrations", () => {
  it("shows the illustration with a description of which page it belongs to", async () => {
    serveStory({ pages: [aPage({ pageNo: 1, imageUrl: "https://example.test/1.png" })] });
    render(<StoryPage />);

    expect(await screen.findByRole("img", { name: "Illustrazione della pagina 1" })).toHaveAttribute(
      "src",
      "https://example.test/1.png",
    );
  });

  it("says an illustration is still coming while the page is pending", async () => {
    serveStory({ pages: [aPage({ pageNo: 1, imageUrl: null, status: "Pending" })] });
    render(<StoryPage />);

    expect(await screen.findByText(/Illustrazione in arrivo/)).toBeInTheDocument();
  });

  it("says an illustration is unavailable when its page failed", async () => {
    serveStory({ pages: [aPage({ pageNo: 1, imageUrl: null, status: "Failed" })] });
    render(<StoryPage />);

    expect(await screen.findByText(/Illustrazione non disponibile/)).toBeInTheDocument();
  });
});
