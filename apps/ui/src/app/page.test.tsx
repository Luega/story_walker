import { render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { worker } from "@/test/handlers";
import { aStorySummary } from "@/test/factories";
import DashboardPage from "./page";

// The dashboard mounts NewStoryModal, which needs Next's router. Only the navigation is faked;
// everything else in these tests goes through real HTTP to MSW.
const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

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

beforeEach(() => push.mockClear());

describe("listing", () => {
  it("shows the stories the API returns", async () => {
    serveStories(aStorySummary({ title: "La luna curiosa" }), aStorySummary({ title: "Il bosco" }));
    render(<DashboardPage />);

    expect(await screen.findByRole("link", { name: "La luna curiosa" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Il bosco" })).toBeInTheDocument();
  });

  it("invites the user to start when there are no stories", async () => {
    serveStories();
    render(<DashboardPage />);

    expect(await screen.findByText(/Nessuna storia ancora/)).toBeInTheDocument();
  });

  it("reports a failure to load", async () => {
    worker.use(http.get("/api/stories", () => new HttpResponse(null, { status: 500 })));
    render(<DashboardPage />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/500/);
  });

  it("opens the new-story modal", async () => {
    serveStories();
    render(<DashboardPage />);
    await screen.findByText(/Nessuna storia ancora/);

    await userEvent.click(screen.getByRole("button", { name: "+ Nuova storia" }));

    expect(screen.getByRole("heading", { name: "Nuova storia" })).toBeInTheDocument();
  });
});

describe("deleting a story", () => {
  it("asks for confirmation, then removes the card without reloading the list", async () => {
    const story = aStorySummary({ title: "Da eliminare" });
    const list = serveStories(story);
    let deleted: string | null = null;
    worker.use(
      http.delete("/api/stories/:id", ({ params }) => {
        deleted = String(params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Da eliminare" });

    await userEvent.click(screen.getByRole("button", { name: "Elimina «Da eliminare»" }));
    await userEvent.click(screen.getByRole("button", { name: "Elimina" }));

    await waitFor(() => expect(screen.queryByText("Da eliminare")).not.toBeInTheDocument());
    expect(deleted).toBe(story.id);
    // The list is dropped locally; refetching would be a wasted round trip.
    expect(list.calls).toBe(1);
  });

  it("keeps the story and explains why when the delete fails", async () => {
    serveStories(aStorySummary({ title: "Resiste" }));
    worker.use(http.delete("/api/stories/:id", () => new HttpResponse(null, { status: 500 })));
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Resiste" });

    await userEvent.click(screen.getByRole("button", { name: "Elimina «Resiste»" }));
    await userEvent.click(screen.getByRole("button", { name: "Elimina" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/500/);
    expect(screen.getByRole("link", { name: "Resiste" })).toBeInTheDocument();
  });

  it("leaves the story alone when the confirmation is dismissed", async () => {
    serveStories(aStorySummary({ title: "Salva" }));
    let deleteCalls = 0;
    worker.use(
      http.delete("/api/stories/:id", () => {
        deleteCalls += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Salva" });

    await userEvent.click(screen.getByRole("button", { name: "Elimina «Salva»" }));
    await userEvent.click(screen.getByRole("button", { name: "Annulla" }));

    expect(deleteCalls).toBe(0);
    expect(screen.getByRole("link", { name: "Salva" })).toBeInTheDocument();
  });
});

describe("renaming a story", () => {
  it("sends one PATCH and shows the title the server stored", async () => {
    const story = aStorySummary({ title: "Titolo vecchio" });
    serveStories(story);
    const patches: unknown[] = [];
    worker.use(
      http.patch("/api/stories/:id", async ({ request }) => {
        patches.push(await request.json());
        // The server trims; the dashboard must display this, not the raw input.
        return HttpResponse.json({
          ...story,
          title: "Titolo nuovo",
          targetAge: 5,
          artStyle: "acquerello",
          pages: [],
        });
      }),
    );
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Titolo vecchio" });

    await userEvent.click(screen.getByRole("button", { name: "Rinomina «Titolo vecchio»" }));
    const input = screen.getByRole("textbox", { name: "Titolo della storia" });
    await userEvent.fill(input, "  Titolo nuovo  ");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("link", { name: "Titolo nuovo" })).toBeInTheDocument();
    // One request, not two: the blur fired by disabling the input must not send a second PATCH.
    expect(patches).toEqual([{ title: "Titolo nuovo" }]);
  });

  it("reports a failed rename and keeps the edit open", async () => {
    serveStories(aStorySummary({ title: "Titolo vecchio" }));
    worker.use(http.patch("/api/stories/:id", () => new HttpResponse(null, { status: 500 })));
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Titolo vecchio" });

    await userEvent.click(screen.getByRole("button", { name: "Rinomina «Titolo vecchio»" }));
    const input = screen.getByRole("textbox", { name: "Titolo della storia" });
    await userEvent.fill(input, "Titolo nuovo");
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByRole("alert")).toHaveTextContent(/500/);
    expect(screen.getByRole("textbox", { name: "Titolo della storia" })).toHaveValue("Titolo nuovo");
  });
});

describe("polling", () => {
  // POLL_MS is 5s in page.tsx, so these two tests wait in real time. They are the slowest in the
  // suite; the alternative (faking timers) also fakes the timing of the fetches they depend on.
  it("keeps refreshing while a story is still generating", async () => {
    const list = serveStories(aStorySummary({ status: "Generating", title: "In corso" }));
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "In corso" });

    await waitFor(() => expect(list.calls).toBeGreaterThan(1), { timeout: 8000 });
  }, 10_000);

  it("stops refreshing once nothing is generating", async () => {
    const list = serveStories(aStorySummary({ status: "Complete", title: "Finita" }));
    render(<DashboardPage />);
    await screen.findByRole("link", { name: "Finita" });

    await new Promise((r) => setTimeout(r, 6000));

    expect(list.calls).toBe(1);
  }, 10_000);
});
