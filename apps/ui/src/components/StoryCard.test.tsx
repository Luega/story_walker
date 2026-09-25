import { render, screen } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { describe, expect, it, vi, type Mock } from "vitest";
import type { StorySummary } from "@/lib/api";
import { aStorySummary } from "@/test/factories";
import { StoryCard } from "./StoryCard";

type RenameFn = (story: StorySummary, title: string) => Promise<void>;

/** A promise the test resolves by hand, to hold a rename open while asserting what happens meanwhile. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

function renderCard({
  onRename = vi.fn<RenameFn>().mockResolvedValue(undefined),
  ...overrides
}: Parameters<typeof aStorySummary>[0] & { onRename?: Mock<RenameFn> } = {}) {
  const story = aStorySummary(overrides);
  const onDelete = vi.fn();
  render(<StoryCard story={story} onDelete={onDelete} onRename={onRename} />);

  const rename = () =>
    userEvent.click(screen.getByRole("button", { name: `Rename “${story.title}”` }));

  return { story, onDelete, onRename, rename };
}

const titleInput = () => screen.getByRole("textbox", { name: "Story title" });

/** The card itself: the h2 sits directly inside it. */
const cardOf = (title: string) => screen.getByRole("heading", { name: title }).parentElement;

/** The overlay that marks a failed story inactive, if the card has one. */
const overlayOf = (title: string) => cardOf(title)!.querySelector(":scope > .bg-red-500\\/15");

describe("reading", () => {
  it("links to a story that is ready", () => {
    const { story } = renderCard({ title: "The Curious Moon", status: "Complete" });

    expect(screen.getByRole("link", { name: "The Curious Moon" })).toHaveAttribute(
      "href",
      `/stories/${story.id}`,
    );
  });

  // Only a settled story is worth opening: the others have no pages to read yet.
  it.each(["Draft", "Generating", "Failed"] as const)(
    "does not link to a story that is %s",
    (status) => {
      renderCard({ title: "The Curious Moon", status });

      expect(screen.queryByRole("link")).not.toBeInTheDocument();
      // The title is still shown, just not as a way in.
      expect(screen.getByRole("heading", { name: "The Curious Moon" })).toBeInTheDocument();
    },
  );

  it("covers a failed story with a light red overlay", () => {
    renderCard({ title: "Gone wrong", status: "Failed" });

    expect(overlayOf("Gone wrong")).not.toBeNull();
  });

  it("leaves a story that can be read uncovered", () => {
    renderCard({ title: "All well", status: "Complete" });

    expect(overlayOf("All well")).toBeNull();
  });

  it("names its actions after the story, so they are distinguishable in a list", () => {
    renderCard({ title: "The Curious Moon" });

    expect(screen.getByRole("button", { name: "Rename “The Curious Moon”" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Delete “The Curious Moon”" })).toBeInTheDocument();
  });
});

describe("deleting", () => {
  it("reports the story to delete without deciding anything itself", async () => {
    const { story, onDelete } = renderCard();

    await userEvent.click(screen.getByRole("button", { name: `Delete “${story.title}”` }));

    expect(onDelete).toHaveBeenCalledExactlyOnceWith(story);
  });
});

// A failed story can only be deleted (ADR-0026).
describe("a failed story", () => {
  it("cannot be renamed", () => {
    renderCard({ title: "Gone wrong", status: "Failed" });

    expect(screen.queryByRole("button", { name: "Rename “Gone wrong”" })).not.toBeInTheDocument();
  });

  it("can still be deleted", async () => {
    const { story, onDelete } = renderCard({ title: "Gone wrong", status: "Failed" });

    await userEvent.click(screen.getByRole("button", { name: "Delete “Gone wrong”" }));

    expect(onDelete).toHaveBeenCalledExactlyOnceWith(story);
  });

  it("shows its delete action without waiting for a hover, since it is the only one", () => {
    renderCard({ title: "Gone wrong", status: "Failed" });

    expect(screen.getByRole("button", { name: "Delete “Gone wrong”" })).not.toHaveClass("sm:opacity-0");
  });
});

describe("renaming", () => {
  it("opens an input seeded with the current title", async () => {
    const { story, rename } = renderCard();

    await rename();

    expect(titleInput()).toHaveValue(story.title);
    // The link is gone while editing: its full-card overlay would otherwise swallow the clicks.
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("saves the new title on Enter", async () => {
    const { story, onRename, rename } = renderCard();

    await rename();
    await userEvent.fill(titleInput(), "New title");
    await userEvent.keyboard("{Enter}");

    expect(onRename).toHaveBeenCalledExactlyOnceWith(story, "New title");
  });

  it("trims the title before saving", async () => {
    const { story, onRename, rename } = renderCard();

    await rename();
    await userEvent.fill(titleInput(), "   New title   ");
    await userEvent.keyboard("{Enter}");

    expect(onRename).toHaveBeenCalledExactlyOnceWith(story, "New title");
  });

  it("saves when the input loses focus", async () => {
    const { story, onRename, rename } = renderCard();

    await rename();
    await userEvent.fill(titleInput(), "Saved on blur");
    await userEvent.click(document.body);

    expect(onRename).toHaveBeenCalledExactlyOnceWith(story, "Saved on blur");
  });

  it("discards the edit on Escape without touching the server", async () => {
    const { story, onRename, rename } = renderCard();

    await rename();
    await userEvent.fill(titleInput(), "Second thoughts");
    await userEvent.keyboard("{Escape}");

    expect(onRename).not.toHaveBeenCalled();
    expect(screen.getByRole("link", { name: story.title })).toBeInTheDocument();
  });

  it("does not save an empty title", async () => {
    const { story, onRename, rename } = renderCard();

    await rename();
    await userEvent.fill(titleInput(), "   ");
    await userEvent.keyboard("{Enter}");

    expect(onRename).not.toHaveBeenCalled();
    expect(screen.getByRole("link", { name: story.title })).toBeInTheDocument();
  });

  it("does not call the server when the title is unchanged", async () => {
    const { onRename, rename } = renderCard();

    await rename();
    await userEvent.keyboard("{Enter}");

    expect(onRename).not.toHaveBeenCalled();
  });

  it("saves once even though disabling the input fires a blur mid-save", async () => {
    // The regression this guards: disabling the input steals focus, onBlur runs save() again, and
    // a second PATCH goes out. Holding the rename open is what makes that window observable.
    const pending = deferred<void>();
    const { onRename, rename } = renderCard({
      onRename: vi.fn<RenameFn>().mockReturnValue(pending.promise),
    });

    await rename();
    await userEvent.fill(titleInput(), "New title");
    await userEvent.keyboard("{Enter}");

    expect(onRename).toHaveBeenCalledTimes(1);

    pending.resolve();
  });

  it("keeps the typed title in the input when saving fails", async () => {
    const { rename } = renderCard({
      onRename: vi.fn<RenameFn>().mockRejectedValue(new Error("boom")),
    });

    await rename();
    await userEvent.fill(titleInput(), "A title worth keeping");
    await userEvent.keyboard("{Enter}");

    await expect.element(titleInput()).toHaveValue("A title worth keeping");
  });
});
