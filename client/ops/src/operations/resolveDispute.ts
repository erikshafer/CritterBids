import { z } from "zod";

import type { StaffFetch } from "@/auth/staffApi";

// Narrative 008 Moment 2 — the one place the operator ACTS. Posts the existing StaffOnly
// ResolveDispute command endpoint (src/CritterBids.Obligations/ResolveDisputeEndpoint.cs).
// The command IS the request body (wolverine-http-frontend-contract §1: JSON body with
// Content-Type, camelCase keys); the endpoint returns 202 Accepted with NO body — the saga
// applies the resolution asynchronously, so success here means "accepted", not "resolved".
// The card leaving the dispute queue via the DisputeResolved push re-query is the success
// signal (ADR 026) — this module performs no cache writes.
//
// Refund/Closed stay un-surfaced (refund compensation is a recorded M6/M7 non-goal; the
// narrative's beat is the Extension path — the one non-terminal resolution), so the seam
// hardcodes resolutionType: "Extension".

export interface ResolveDisputeInput {
  obligationId: string;
  disputeId: string;
}

/** Tolerant ProblemDetails projection — a 5xx may carry no body at all (skill §4). */
const problemDetailsSchema = z.object({
  title: z.string().nullish(),
  detail: z.string().nullish(),
});

export class ResolveDisputeError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ResolveDisputeError";
    this.status = status;
  }
}

export type ResolveDisputeFn = (
  staffFetch: StaffFetch,
  input: ResolveDisputeInput,
) => Promise<void>;

export const resolveDisputeWithExtension: ResolveDisputeFn = async (
  staffFetch,
  { obligationId, disputeId },
) => {
  const response = await staffFetch("/api/obligations/disputes/resolve", {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ obligationId, disputeId, resolutionType: "Extension" }),
  });

  if (response.ok) {
    // 202 Accepted, no body — never attempt a parse (wolverine-http-frontend-contract).
    return;
  }

  // staffFetch has already funnelled a 401 into clear-token + re-gate; the throw below still
  // runs so the in-flight mutation settles as an error rather than hanging.
  throw new ResolveDisputeError(response.status, await rejectionMessage(response));
};

async function rejectionMessage(response: Response): Promise<string> {
  try {
    const problem = problemDetailsSchema.safeParse(await response.json());
    if (problem.success) {
      const text = problem.data.detail ?? problem.data.title;
      if (text != null && text !== "") {
        return text;
      }
    }
  } catch {
    // Not JSON (or empty) — fall through to the generic message.
  }
  return `The resolve request failed with ${response.status}. The dispute is unchanged — try again.`;
}
