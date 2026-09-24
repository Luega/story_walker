// The visitor's answer to the analytics-cookie question (ADR-0023). Two readers: the banner, which
// asks, and instrumentation-client.ts, which turns the Application Insights cookies on or off.
// They are separate bundles, so they talk through localStorage (the answer that survives reloads)
// and a window event (the change, as it happens) rather than through shared module state.

export type CookieConsent = "accepted" | "rejected";

const STORAGE_KEY = "storywalker.cookie-consent";

/** Dispatched on window with the new {@link CookieConsent} as `detail` whenever it is saved. */
export const COOKIE_CONSENT_EVENT = "storywalker:cookie-consent";

/** The saved answer, or null when the visitor has not answered yet. */
export function readCookieConsent(): CookieConsent | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === "accepted" || value === "rejected" ? value : null;
  } catch {
    // Storage blocked (private mode, a strict browser setting): treat it as no answer. The banner
    // will ask again next visit, which is the safe side of the mistake.
    return null;
  }
}

export function saveCookieConsent(consent: CookieConsent) {
  try {
    localStorage.setItem(STORAGE_KEY, consent);
  } catch {
    // Still announce it: the choice holds for this page even if it cannot be remembered.
  }
  window.dispatchEvent(new CustomEvent<CookieConsent>(COOKIE_CONSENT_EVENT, { detail: consent }));
}
