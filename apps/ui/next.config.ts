import type { NextConfig } from "next";

// /api/* is served by the Route Handler at src/app/api/[...path]/route.ts, which attaches the
// Clerk token before forwarding to the ASP.NET Core API. The browser only ever talks to this
// origin, so the API still needs no CORS policy.
const nextConfig: NextConfig = {
  // `next build` also writes .next/standalone: a server.js plus only the node_modules files it
  // traces as used. The Dockerfile ships that instead of the full dependency tree.
  output: "standalone",
};

export default nextConfig;
