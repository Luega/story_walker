import { http, HttpResponse } from "msw";
import { setupWorker } from "msw/browser";

// The UI talks to relative /api/* URLs (the rewrite in next.config.ts), so the handlers do too —
// MSW resolves them against the page origin.
export const API = "/api";

/**
 * Default: an empty dashboard, with the whole weekly limit still to use. Tests declare the traffic they care about with `worker.use(...)`,
 * which keeps each test readable and makes an unexpected request loud instead of silently served.
 */
export const defaultHandlers = [
  http.get(`${API}/stories`, () => HttpResponse.json([])),
  http.get(`${API}/stories/:id`, () => new HttpResponse(null, { status: 404 })),
  // Room to spare, so a test only meets the weekly limit when it asks for it (ADR-0030).
  http.get(`${API}/me/quota`, () =>
    HttpResponse.json({ limit: 7, used: 0, remaining: 7, resetsAt: "2026-09-28T00:00:00Z" }),
  ),
];

export const worker = setupWorker(...defaultHandlers);
