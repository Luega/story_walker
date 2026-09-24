import { render, screen } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { beforeEach, describe, expect, it } from "vitest";
import { COOKIE_CONSENT_EVENT, readCookieConsent } from "@/lib/cookieConsent";
import { CookieConsent } from "./CookieConsent";

const banner = () => screen.queryByRole("region", { name: "Analytics cookies" });
const acceptButton = () => screen.getByRole("button", { name: "Accept" });
const rejectButton = () => screen.getByRole("button", { name: "Reject" });
const preferencesLink = () => screen.getByRole("button", { name: "Cookie preferences" });

beforeEach(() => localStorage.clear());

describe("when browser telemetry is not configured", () => {
  it("renders nothing, because there are no analytics cookies to ask about", () => {
    // The test build has no NEXT_PUBLIC_ connection string, so the default is "not configured".
    const { container } = render(<CookieConsent />);

    expect(container).toBeEmptyDOMElement();
  });
});

describe("asking", () => {
  it("shows the banner to a visitor who has not answered", () => {
    render(<CookieConsent telemetryConfigured />);

    expect(banner()).toBeInTheDocument();
  });

  it("does not show it again once answered", () => {
    localStorage.setItem("storywalker.cookie-consent", "rejected");

    render(<CookieConsent telemetryConfigured />);

    expect(banner()).not.toBeInTheDocument();
    expect(preferencesLink()).toBeInTheDocument();
  });

  it("makes Reject exactly as prominent as Accept", () => {
    render(<CookieConsent telemetryConfigured />);

    expect(rejectButton().className).toBe(acceptButton().className);
  });
});

describe("answering", () => {
  it.each([
    ["Accept", "accepted"],
    ["Reject", "rejected"],
  ] as const)("%s saves the answer, announces it and closes", async (label, consent) => {
    const announced: unknown[] = [];
    const listener = (e: Event) => announced.push((e as CustomEvent).detail);
    window.addEventListener(COOKIE_CONSENT_EVENT, listener);

    render(<CookieConsent telemetryConfigured />);
    await userEvent.click(screen.getByRole("button", { name: label }));

    window.removeEventListener(COOKIE_CONSENT_EVENT, listener);
    expect(readCookieConsent()).toBe(consent);
    // The event is how instrumentation-client.ts learns to switch the cookies on or off.
    expect(announced).toEqual([consent]);
    expect(banner()).not.toBeInTheDocument();
  });

  it("lets the visitor change their mind from the footer", async () => {
    localStorage.setItem("storywalker.cookie-consent", "accepted");
    render(<CookieConsent telemetryConfigured />);

    await userEvent.click(preferencesLink());
    expect(banner()).toBeInTheDocument();

    await userEvent.click(rejectButton());
    expect(readCookieConsent()).toBe("rejected");
    expect(banner()).not.toBeInTheDocument();
  });
});
