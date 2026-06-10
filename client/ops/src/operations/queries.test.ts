import { describe, expect, it, vi } from "vitest";

import type { StaffFetch } from "@/auth/staffApi";
import {
  listingTitleQueryOptions,
  lotBoardQueryOptions,
} from "@/operations/queries";

// Query-function tests with a fake staffFetch (the injectable seam — no module mocks, no
// global fetch patching). These verify the request side (URL, Accept header, going through
// staffFetch at all) and the parse boundary; the live smoke verifies the real wire.

const lotBoardRow = {
  listingId: "11111111-0000-0000-0000-000000000001",
  sellerId: "22222222-0000-0000-0000-000000000002",
  title: "Boxed vintage synthesizer",
  format: "Flash",
  startingBid: 10,
  reservePrice: null,
  buyItNow: null,
  feePercentage: 0.1,
  scheduledCloseAt: null,
  currentBid: 42,
  bidCount: 3,
  hammerPrice: null,
  winnerId: null,
  passReason: null,
  withdrawnBy: null,
  withdrawalReason: null,
  status: "Open",
  lastUpdatedAt: "2026-06-10T15:04:05Z",
  id: "11111111-0000-0000-0000-000000000001", // the record's computed identity — stripped, not rejected
};

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("board query functions", () => {
  it("requests the board through staffFetch with Accept: application/json and parses rows", async () => {
    const staffFetch = vi.fn(async () =>
      jsonResponse([lotBoardRow]),
    ) as unknown as StaffFetch & ReturnType<typeof vi.fn>;

    const options = lotBoardQueryOptions(staffFetch);
    expect(options.queryKey).toEqual(["operations", "lot-board"]);

    const rows = await options.queryFn!({} as never);
    expect(staffFetch).toHaveBeenCalledWith("/api/operations/lot-board", {
      headers: { Accept: "application/json" },
    });
    expect(rows).toHaveLength(1);
    expect(rows[0]!.title).toBe("Boxed vintage synthesizer");
    expect(rows[0]!.currentBid).toBe(42);
    // The computed `id` the backend serializes is stripped by the schema, not an error.
    expect("id" in rows[0]!).toBe(false);
  });

  it("parses [] (the empty board is a state, never an error)", async () => {
    const staffFetch = (async () => jsonResponse([])) as StaffFetch;
    const rows = await lotBoardQueryOptions(staffFetch).queryFn!({} as never);
    expect(rows).toEqual([]);
  });

  it("rejects a shape-drifted row at the boundary", async () => {
    const staffFetch = (async () =>
      jsonResponse([{ ...lotBoardRow, bidCount: "three" }])) as StaffFetch;
    await expect(
      lotBoardQueryOptions(staffFetch).queryFn!({} as never),
    ).rejects.toThrow();
  });

  it("throws with the status code on a non-2xx (staffFetch already funnelled any 401)", async () => {
    const staffFetch = (async () => jsonResponse({}, 500)) as StaffFetch;
    await expect(
      lotBoardQueryOptions(staffFetch).queryFn!({} as never),
    ).rejects.toThrow(/500/);
  });
});

describe("listingTitleQueryOptions (the render-time Title join)", () => {
  it("resolves the title from GET /api/listings/{id}", async () => {
    const fetchImpl = vi.fn(async () =>
      jsonResponse({ title: "Boxed vintage synthesizer", status: "Open" }),
    ) as unknown as typeof fetch;

    const options = listingTitleQueryOptions("abc-123", fetchImpl);
    expect(options.queryKey).toEqual(["listing", "abc-123"]);

    const title = await options.queryFn!({} as never);
    expect(fetchImpl).toHaveBeenCalledWith("/api/listings/abc-123", {
      headers: { Accept: "application/json" },
    });
    expect(title).toBe("Boxed vintage synthesizer");
  });

  it("resolves null on a 404 — a stable answer, not a retryable failure", async () => {
    const fetchImpl = (async () =>
      new Response(null, { status: 404 })) as typeof fetch;
    const title = await listingTitleQueryOptions("gone", fetchImpl).queryFn!(
      {} as never,
    );
    expect(title).toBeNull();
  });
});
