import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll } from "vitest";
import { worker } from "./handlers";

beforeAll(async () => {
  await worker.start({
    quiet: true,
    // Only /api/* is ours. Vitest's own module and asset traffic shares this origin, so erroring on
    // every unhandled request would flag the test runner itself; erroring on just /api/* still
    // catches a typo'd URL or a request no test expected.
    onUnhandledRequest(request, print) {
      if (new URL(request.url).pathname.startsWith("/api/")) print.error();
    },
  });
});

afterEach(() => {
  cleanup();
  worker.resetHandlers();
});

afterAll(() => worker.stop());
