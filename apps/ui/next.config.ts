import type { NextConfig } from "next";

// /api/* is served by the Route Handler at src/app/api/[...path]/route.ts, which attaches the
// Clerk token before forwarding to the ASP.NET Core API. The browser only ever talks to this
// origin, so the API still needs no CORS policy.
const nextConfig: NextConfig = {};

export default nextConfig;
