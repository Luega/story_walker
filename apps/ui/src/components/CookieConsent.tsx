"use client";

import { useState, useSyncExternalStore } from "react";
import {
  COOKIE_CONSENT_EVENT,
  readCookieConsent,
  saveCookieConsent,
  type CookieConsent,
} from "@/lib/cookieConsent";

// Browser telemetry is opt-in at build time (instrumentation-client.ts); with it off there are no
// analytics cookies, so there is nothing to ask about.
const browserTelemetryConfigured = Boolean(
  process.env.NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING?.trim(),
);

/**
 * Asks once for consent to the Application Insights analytics cookies (ADR-0023), and keeps a
 * "Cookie preferences" link in the footer to change the answer later. Accept and Reject are styled
 * identically on purpose: refusing must be as easy as accepting.
 */
export function CookieConsent({
  telemetryConfigured = browserTelemetryConfigured,
}: {
  telemetryConfigured?: boolean;
}) {
  // "unknown" on the server and during hydration: the answer lives in localStorage, which the
  // server render cannot see, and guessing would flash the banner at people who already answered.
  const consent = useSyncExternalStore(subscribe, readCookieConsent, () => "unknown" as const);
  const [reopened, setReopened] = useState(false);

  if (!telemetryConfigured) return null;

  const open = consent === null || reopened;

  function choose(choice: CookieConsent) {
    saveCookieConsent(choice);
    setReopened(false);
  }

  return (
    <>
      {/* Sits in the layout's footer, next to the privacy link. */}
      <button type="button" onClick={() => setReopened(true)} className="hover:text-accent">
        Cookie preferences
      </button>

      {open && (
        <section
          aria-labelledby="cookie-consent-heading"
          className="fixed inset-x-4 bottom-4 z-50 mx-auto max-w-2xl rounded-[2rem] border-2 border-border bg-surface p-6 text-foreground shadow-2xl"
        >
          <h2 id="cookie-consent-heading" className="font-display text-lg font-semibold">
            Analytics cookies
          </h2>
          <p className="mt-2 text-sm leading-relaxed text-muted">
            May we set two cookies that let us count visitors and visits? They hold random
            identifiers only — nothing about you or your stories. Sign-in cookies are always on,
            because the site cannot work without them.
          </p>
          <div className="mt-4 flex justify-end gap-2">
            <button
              type="button"
              onClick={() => choose("rejected")}
              className="btn-secondary"
            >
              Reject
            </button>
            <button
              type="button"
              onClick={() => choose("accepted")}
              className="btn-secondary"
            >
              Accept
            </button>
          </div>
        </section>
      )}
    </>
  );
}

// Re-read on our own event (this tab) and on "storage" (an answer given in another tab).
function subscribe(onChange: () => void) {
  window.addEventListener(COOKIE_CONSENT_EVENT, onChange);
  window.addEventListener("storage", onChange);
  return () => {
    window.removeEventListener(COOKIE_CONSENT_EVENT, onChange);
    window.removeEventListener("storage", onChange);
  };
}
