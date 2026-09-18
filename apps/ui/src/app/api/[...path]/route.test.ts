import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const getToken = vi.fn();
vi.mock("@clerk/nextjs/server", () => ({ auth: () => Promise.resolve({ getToken }) }));

const { GET, POST, DELETE } = await import("./route");

const params = (...path: string[]) => ({ params: Promise.resolve({ path }) });

describe("API proxy route", () => {
  beforeEach(() => {
    getToken.mockResolvedValue("test-token");
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  it("forwards the session token as a Bearer header", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response("[]", { status: 200 }));

    await GET(new Request("http://localhost:3000/api/stories"), params("stories"));

    const [url, init] = vi.mocked(fetch).mock.calls[0];
    expect(String(url)).toBe("http://localhost:5080/stories");
    expect(new Headers(init!.headers).get("authorization")).toBe("Bearer test-token");
  });

  it("propagates the method, body and status code", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 204 }));

    const response = await DELETE(
      new Request("http://localhost:3000/api/stories/abc", { method: "DELETE" }),
      params("stories", "abc"),
    );

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init!.method).toBe("DELETE");
    expect(response.status).toBe(204);
  });

  it("forwards the request body on a POST", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response("{}", { status: 200 }));

    await POST(
      new Request("http://localhost:3000/api/stories/conversations", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ message: "hello" }),
      }),
      params("stories", "conversations"),
    );

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init!.body).toBe('{"message":"hello"}');
  });

  it("fails closed when there is no session", async () => {
    getToken.mockResolvedValue(null);

    const response = await GET(new Request("http://localhost:3000/api/stories"), params("stories"));

    expect(response.status).toBe(401);
    expect(fetch).not.toHaveBeenCalled();
  });
});
