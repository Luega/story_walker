import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import PrivacyPage from "./page";

// The draft privacy page (ADR-0028). What it must never lose, whatever the final wording.
describe("the privacy page", () => {
  it("says what is kept, who may see it, and that the parent can delete it", () => {
    render(<PrivacyPage />);

    expect(screen.getByRole("heading", { level: 1, name: "Privacy" })).toBeInTheDocument();
    expect(screen.getByText(/conversations with the story agent/i)).toBeInTheDocument();
    expect(screen.getByText(/technical staff/i)).toBeInTheDocument();
    expect(screen.getByText(/delete a conversation/i)).toBeInTheDocument();
  });
});
