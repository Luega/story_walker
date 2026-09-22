import { render, screen, waitFor } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { worker } from "@/test/handlers";
import { aUserSettings } from "@/test/factories";
import type { UserSettings } from "@/lib/api";
import SettingsPage from "./page";

const SETTINGS = "/api/me/settings";

/** Serves the given settings and records whatever the page saves back. */
function serve(settings: Partial<UserSettings> = {}) {
  const saves: UserSettings[] = [];
  worker.use(
    http.get(SETTINGS, () => HttpResponse.json(aUserSettings(settings))),
    http.patch(SETTINGS, async ({ request }) => {
      const body = (await request.json()) as UserSettings;
      saves.push(body);
      return HttpResponse.json(body);
    }),
  );
  return saves;
}

const kidsBox = () => screen.getByLabelText(/Who the stories are for/);
const artBox = () => screen.getByLabelText(/The look of the illustrations/);
const save = () => screen.getByRole("button", { name: /Save/ });

describe("loading the saved context", () => {
  it("shows each answer in its own box", async () => {
    serve({ kids: "Mia is 5 and loves dinosaurs", artStyle: "soft watercolour" });
    render(<SettingsPage />);

    expect(await screen.findByDisplayValue("Mia is 5 and loves dinosaurs")).toBeInTheDocument();
    expect(screen.getByDisplayValue("soft watercolour")).toBeInTheDocument();
  });

  it("leaves a box empty when that question has never been answered", async () => {
    serve({ kids: null });
    render(<SettingsPage />);

    await waitFor(() => expect(artBox()).toHaveValue("soft watercolour"));
    expect(kidsBox()).toHaveValue("");
  });

  it("reports a failure to load rather than showing empty boxes as if they were saved", async () => {
    worker.use(http.get(SETTINGS, () => new HttpResponse(null, { status: 500 })));
    render(<SettingsPage />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/500/);
  });
});

describe("saving", () => {
  it("sends every answer, including the ones left untouched", async () => {
    const saves = serve({ kids: "Mia is 5" });
    render(<SettingsPage />);
    await waitFor(() => expect(kidsBox()).toHaveValue("Mia is 5"));

    await userEvent.fill(kidsBox(), "Mia is 6 now");
    await userEvent.click(save());

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0]).toEqual({
      kids: "Mia is 6 now",
      story: "Short and gentle, always a kind ending",
      characters: "Pip, a small grey kitten",
      artStyle: "soft watercolour",
    });
  });

  it("sends a cleared answer as null so it is actually cleared", async () => {
    const saves = serve({ kids: "Mia is 5" });
    render(<SettingsPage />);
    await waitFor(() => expect(kidsBox()).toHaveValue("Mia is 5"));

    await userEvent.clear(kidsBox());
    await userEvent.click(save());

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].kids).toBeNull();
  });

  it("confirms the save", async () => {
    serve();
    render(<SettingsPage />);
    await waitFor(() => expect(kidsBox()).toHaveValue("Mia is 5 and loves dinosaurs"));

    await userEvent.click(save());

    expect(await screen.findByRole("status")).toHaveTextContent(/Saved/);
  });

  it("shows what the server stored, not what was typed", async () => {
    worker.use(
      http.get(SETTINGS, () => HttpResponse.json(aUserSettings({ kids: "" }))),
      http.patch(SETTINGS, () => HttpResponse.json(aUserSettings({ kids: "Mia is 5" }))),
    );
    render(<SettingsPage />);
    await waitFor(() => expect(artBox()).toHaveValue("soft watercolour"));

    await userEvent.fill(kidsBox(), "   Mia is 5   ");
    await userEvent.click(save());

    await waitFor(() => expect(kidsBox()).toHaveValue("Mia is 5"));
  });

  it("surfaces the server's complaint about an answer that is too long", async () => {
    worker.use(
      http.get(SETTINGS, () => HttpResponse.json(aUserSettings())),
      http.patch(SETTINGS, () =>
        HttpResponse.json(
          { status: 400, errors: { artStyle: ["Keep this to 600 characters or fewer."] } },
          { status: 400 },
        ),
      ),
    );
    render(<SettingsPage />);
    await waitFor(() => expect(kidsBox()).toHaveValue("Mia is 5 and loves dinosaurs"));

    await userEvent.click(save());

    expect(await screen.findByRole("alert")).toHaveTextContent(/600 characters or fewer/);
  });

  it("keeps what the user typed when the save fails", async () => {
    worker.use(
      http.get(SETTINGS, () => HttpResponse.json(aUserSettings({ kids: "" }))),
      http.patch(SETTINGS, () => new HttpResponse(null, { status: 500 })),
    );
    render(<SettingsPage />);
    await waitFor(() => expect(artBox()).toHaveValue("soft watercolour"));

    await userEvent.fill(kidsBox(), "Mia is 5");
    await userEvent.click(save());

    await screen.findByRole("alert");
    expect(kidsBox()).toHaveValue("Mia is 5");
  });
});

describe("the length cap", () => {
  it("refuses to save an over-long answer, without asking the server", async () => {
    const saves = serve({ kids: "" });
    render(<SettingsPage />);
    await waitFor(() => expect(artBox()).toHaveValue("soft watercolour"));

    await userEvent.fill(kidsBox(), "x".repeat(601));

    expect(save()).toBeDisabled();
    expect(saves).toHaveLength(0);
  });

  it("counts what is left and says so once the answer is too long", async () => {
    serve({ kids: "" });
    render(<SettingsPage />);
    await waitFor(() => expect(artBox()).toHaveValue("soft watercolour"));

    await userEvent.fill(kidsBox(), "x".repeat(601));

    expect(screen.getByText("601/600")).toBeInTheDocument();
  });
});
