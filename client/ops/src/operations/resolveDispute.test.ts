import { describe, expect, it, vi } from "vitest";

import type { StaffFetch } from "@/auth/staffApi";
import {
  ResolveDisputeError,
  resolveDisputeWithExtension,
} from "@/operations/resolveDispute";

// The mutation seam with a fake staffFetch (the injectable boundary — no module mocks, no
// global fetch patching). These verify the REQUEST side (URL, method, JSON body with
// Content-Type — wolverine-http-frontend-contract §1) and the response contract (202-no-body
// success without a parse attempt; ProblemDetails-aware failures). The live smoke covers the
// real wire.

const input = {
  obligationId: "11111111-0000-0000-0000-000000000001",
  disputeId: "22222222-0000-0000-0000-000000000002",
};

describe("resolveDisputeWithExtension", () => {
  it("POSTs the ResolveDispute command as a camelCase JSON body with Content-Type", async () => {
    const staffFetch = vi.fn(
      async () => new Response(null, { status: 202 }),
    ) as unknown as StaffFetch & ReturnType<typeof vi.fn>;

    await resolveDisputeWithExtension(staffFetch, input);

    expect(staffFetch).toHaveBeenCalledWith("/api/obligations/disputes/resolve", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json",
      },
      body: JSON.stringify({
        obligationId: input.obligationId,
        disputeId: input.disputeId,
        resolutionType: "Extension",
      }),
    });
  });

  it("treats 202 Accepted with no body as success without attempting a parse", async () => {
    // A null-body Response throws on .json() — resolving proves no parse was attempted.
    const staffFetch = (async () =>
      new Response(null, { status: 202 })) as StaffFetch;
    await expect(
      resolveDisputeWithExtension(staffFetch, input),
    ).resolves.toBeUndefined();
  });

  it("surfaces the ProblemDetails detail on a rejection", async () => {
    const staffFetch = (async () =>
      new Response(
        JSON.stringify({
          title: "Dispute not open",
          detail: "The dispute was already resolved.",
          status: 400,
        }),
        { status: 400, headers: { "Content-Type": "application/problem+json" } },
      )) as StaffFetch;

    const failure = resolveDisputeWithExtension(staffFetch, input);
    await expect(failure).rejects.toBeInstanceOf(ResolveDisputeError);
    await expect(failure).rejects.toThrow("The dispute was already resolved.");
  });

  it("falls back to a generic retry message when a failure carries no parseable body (e.g. a bare 5xx)", async () => {
    const staffFetch = (async () =>
      new Response(null, { status: 500 })) as StaffFetch;

    const failure = resolveDisputeWithExtension(staffFetch, input);
    await expect(failure).rejects.toBeInstanceOf(ResolveDisputeError);
    await expect(failure).rejects.toThrow(/500/);
  });

  it("still throws on a 401 so the in-flight mutation settles (staffFetch funnels the re-gate separately)", async () => {
    const onUnauthorized = vi.fn();
    // Mimic createStaffFetch's behavior: call the funnel, then return the response.
    const staffFetch: StaffFetch = async () => {
      const response = new Response(null, { status: 401 });
      onUnauthorized();
      return response;
    };

    await expect(
      resolveDisputeWithExtension(staffFetch, input),
    ).rejects.toMatchObject({ status: 401 });
    expect(onUnauthorized).toHaveBeenCalled();
  });
});
