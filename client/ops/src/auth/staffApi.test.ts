import { describe, expect, it, vi } from "vitest";

import {
  createStaffFetch,
  STAFF_PROBE_PATH,
  STAFF_TOKEN_HEADER,
  validateStaffToken,
} from "@/auth/staffApi";

function response(status: number): Response {
  return { ok: status >= 200 && status < 300, status } as Response;
}

describe("createStaffFetch", () => {
  it("sends the held token as the X-Staff-Token header (ADR 024 HTTP transport)", async () => {
    const fetchImpl = vi.fn(async () => response(200));
    const staffFetch = createStaffFetch({
      getToken: () => "s3cret-staff-token",
      onUnauthorized: vi.fn(),
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    await staffFetch("/api/operations/lot-board");

    const [, init] = fetchImpl.mock.calls[0] as unknown as [
      string,
      RequestInit,
    ];
    expect(new Headers(init.headers).get(STAFF_TOKEN_HEADER)).toBe(
      "s3cret-staff-token",
    );
  });

  it("preserves caller-supplied headers alongside the token", async () => {
    const fetchImpl = vi.fn(async () => response(200));
    const staffFetch = createStaffFetch({
      getToken: () => "s3cret-staff-token",
      onUnauthorized: vi.fn(),
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    await staffFetch("/api/operations/lot-board", {
      headers: { Accept: "application/json" },
    });

    const [, init] = fetchImpl.mock.calls[0] as unknown as [
      string,
      RequestInit,
    ];
    const headers = new Headers(init.headers);
    expect(headers.get("Accept")).toBe("application/json");
    expect(headers.get(STAFF_TOKEN_HEADER)).toBe("s3cret-staff-token");
  });

  it("funnels a 401 into onUnauthorized (clear-token contract)", async () => {
    const onUnauthorized = vi.fn();
    const staffFetch = createStaffFetch({
      getToken: () => "stale-token",
      onUnauthorized,
      fetchImpl: vi.fn(async () => response(401)) as unknown as typeof fetch,
    });

    const result = await staffFetch("/api/operations/lot-board");

    expect(result.status).toBe(401);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });

  it("does not invoke onUnauthorized for non-401 responses", async () => {
    const onUnauthorized = vi.fn();
    const staffFetch = createStaffFetch({
      getToken: () => "s3cret-staff-token",
      onUnauthorized,
      fetchImpl: vi.fn(async () => response(500)) as unknown as typeof fetch,
    });

    await staffFetch("/api/operations/lot-board");

    expect(onUnauthorized).not.toHaveBeenCalled();
  });
});

describe("validateStaffToken", () => {
  it("probes the lot board with the candidate in the X-Staff-Token header", async () => {
    const fetchImpl = vi.fn(async () => response(200));

    const result = await validateStaffToken(
      "candidate-token",
      fetchImpl as unknown as typeof fetch,
    );

    expect(result).toBe("valid");
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [
      string,
      RequestInit,
    ];
    expect(url).toBe(STAFF_PROBE_PATH);
    expect(new Headers(init.headers).get(STAFF_TOKEN_HEADER)).toBe(
      "candidate-token",
    );
  });

  it("maps 401 to invalid", async () => {
    const result = await validateStaffToken(
      "wrong-token",
      vi.fn(async () => response(401)) as unknown as typeof fetch,
    );
    expect(result).toBe("invalid");
  });

  it("maps a 5xx to unreachable (API reached but broken ≠ wrong token)", async () => {
    const result = await validateStaffToken(
      "candidate-token",
      vi.fn(async () => response(503)) as unknown as typeof fetch,
    );
    expect(result).toBe("unreachable");
  });

  it("maps a network failure to unreachable", async () => {
    const result = await validateStaffToken(
      "candidate-token",
      vi.fn(async () => {
        throw new TypeError("fetch failed");
      }) as unknown as typeof fetch,
    );
    expect(result).toBe("unreachable");
  });
});
