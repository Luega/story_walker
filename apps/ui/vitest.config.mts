import { defineConfig } from "vitest/config";
import { playwright } from "@vitest/browser-playwright";

// Tests run in a real Chromium rather than jsdom: jsdom still does not implement
// <dialog>.showModal()/close(), which is exactly the mechanism NewStoryModal and
// ConfirmDeleteDialog are built on. Same trade as ADR-0017 makes for the API suite — slower and
// with a heavier prerequisite, in exchange for tests that fail for the right reasons.
export default defineConfig({
  // Resolves the "@/*" alias from tsconfig.json, natively since Vite 8 (no plugin needed).
  resolve: { tsconfigPaths: true },
  // Serve MSW's worker script without putting it in Next's public/, where it would ship as a
  // production asset.
  publicDir: "src/test/public",
  // Next's client code reads process.env (next/link does at import time), which does not exist in a
  // browser. Replacing it at build time avoids shimming a fake `process` global at runtime, which
  // would make browser-only libraries think they are on Node.
  define: {
    "process.env.NODE_ENV": JSON.stringify("test"),
    "process.env": "{}",
  },
  // Pre-bundle these up front; discovering them mid-run makes Vite reload the page and Vitest warns
  // that it can duplicate or destabilise a test run.
  optimizeDeps: {
    include: ["react", "react-dom", "react/jsx-dev-runtime", "next/link", "next/navigation"],
  },
  test: {
    projects: [
      {
        extends: true,
        test: {
          name: "browser",
          include: ["src/**/*.test.{ts,tsx}"],
          // Setting `exclude` replaces Vitest's default, so node_modules has to be restated.
          exclude: ["**/node_modules/**", "src/app/api/**"],
          setupFiles: ["./src/test/setup.ts"],
          browser: {
            enabled: true,
            provider: playwright(),
            headless: true,
            instances: [{ browser: "chromium" }],
            // Vitest defaults to a 414px-wide phone viewport; the dashboard grid and the `sm:` hover
            // behaviour of the card actions only exist above that, so pin a desktop viewport.
            viewport: { width: 1280, height: 800 },
          },
        },
      },
      {
        // Route Handlers are server code: they cannot run in the browser project, and the one under
        // test decides whether a request leaves the app with credentials, so it gets real coverage.
        extends: true,
        test: {
          name: "node",
          environment: "node",
          include: ["src/app/api/**/*.test.ts"],
        },
      },
    ],
  },
});
