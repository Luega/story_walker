import type { NextConfig } from "next";

// The browser only ever talks to this origin: /api/* is proxied to the ASP.NET Core API, so the
// API needs no CORS policy. API_URL is read at build/start time.
const apiUrl = process.env.API_URL ?? "http://localhost:5080";

const nextConfig: NextConfig = {
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiUrl}/:path*` }];
  },
};

export default nextConfig;
