import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import LandingPage from "./page";

test("invites a visitor to start and links to sign-up", () => {
  render(<LandingPage />);

  expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(/storie/i);
  expect(screen.getByRole("link", { name: /inizia/i })).toHaveAttribute("href", "/sign-up");
});

test("explains the three steps", () => {
  render(<LandingPage />);

  expect(screen.getAllByRole("listitem")).toHaveLength(3);
});
