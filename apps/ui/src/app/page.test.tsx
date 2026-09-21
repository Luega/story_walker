import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";
import LandingPage from "./page";

/** A landmark section of the page, found by the heading that names it. */
const section = (name: string) => within(screen.getByRole("region", { name }));

test("invites a visitor to start and links to sign-up", () => {
  render(<LandingPage />);

  expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(/story/i);
  // The page carries the same invitation twice; the hero's is the first of them.
  expect(screen.getAllByRole("link", { name: /start a story/i })[0]).toHaveAttribute(
    "href",
    "/sign-up",
  );
});

test("offers a second way in at the foot of the page", () => {
  render(<LandingPage />);

  const ways = screen.getAllByRole("link", { name: /start a story/i });
  expect(ways).toHaveLength(2);
  for (const way of ways) expect(way).toHaveAttribute("href", "/sign-up");
});

test("tells the reader where Story Walker came from", () => {
  render(<LandingPage />);

  // The shelf with no book about tonight is the whole point of the story.
  expect(section("Where it came from").getAllByText(/shelf/i).length).toBeGreaterThan(0);
});

test("explains the three steps", () => {
  render(<LandingPage />);

  expect(section("How it works").getAllByRole("listitem")).toHaveLength(3);
});

test("sets out what the goal is and where it is going", () => {
  render(<LandingPage />);

  const why = section("Why Story Walker exists");
  expect(why.getByRole("heading", { name: /the goal/i })).toBeInTheDocument();
  expect(why.getByRole("heading", { name: /the vision/i })).toBeInTheDocument();
});

test("shows what parents say, each quote with the parent behind it", () => {
  render(<LandingPage />);

  const quotes = section("What parents say").getAllByRole("listitem");
  expect(quotes).toHaveLength(3);
  for (const quote of quotes) {
    expect(within(quote).getByRole("blockquote")).toBeInTheDocument();
    expect(within(quote).getByRole("figure")).toHaveAccessibleName(/.+/);
  }
});
