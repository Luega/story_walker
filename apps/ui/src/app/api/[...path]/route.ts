// Server-side proxy to the StoryWalker API (ADR-0002). The browser holds only a Clerk session
// cookie and calls relative /api/* URLs; this handler exchanges that session for a JWT and forwards
// the request. The token never enters the client bundle, and API_URL stays server-only.

import { auth } from "@clerk/nextjs/server";

const API_URL = process.env.API_URL ?? "http://localhost:5080";

type Context = { params: Promise<{ path: string[] }> };

async function proxy(request: Request, { params }: Context): Promise<Response> {
  const { getToken } = await auth();
  const token = await getToken();

  // Fail closed. The API would reject an anonymous request anyway, but answering here keeps a
  // request with no credentials off the network entirely.
  if (!token) return new Response(null, { status: 401 });

  const { path } = await params;
  const target = new URL(`${API_URL}/${path.join("/")}`);
  target.search = new URL(request.url).search;

  const headers = new Headers({ Authorization: `Bearer ${token}` });
  const contentType = request.headers.get("content-type");
  if (contentType) headers.set("content-type", contentType);

  const hasBody = request.method !== "GET" && request.method !== "HEAD";

  const response = await fetch(target, {
    method: request.method,
    headers,
    body: hasBody ? await request.text() : undefined,
    cache: "no-store",
  });

  // 204s carry no body, so passing response.body straight through stays valid.
  const responseHeaders = new Headers();
  const responseType = response.headers.get("content-type");
  if (responseType) responseHeaders.set("content-type", responseType);

  return new Response(response.body, { status: response.status, headers: responseHeaders });
}

export const GET = proxy;
export const POST = proxy;
export const PATCH = proxy;
export const DELETE = proxy;
