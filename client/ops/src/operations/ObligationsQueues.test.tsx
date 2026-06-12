import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { StaffAuthProvider } from "@/auth/StaffAuthContext";
import { Disputes } from "@/operations/ObligationsQueues";
import { listingKey, operationsKeys } from "@/operations/keys";
import type { ResolveDisputeFn } from "@/operations/resolveDispute";
import { ResolveDisputeError } from "@/operations/resolveDispute";
import type { ObligationsRow } from "@/operations/schema";

// Dispute-card render + action tests with a SEEDED query cache (no fetch fires) and the
// component's injectable resolveDispute seam (no module mocks) — the LotBoard.test.tsx pattern
// extended with narrative 008 Moment 2's control. The seam function itself is covered in
// resolveDispute.test.ts; the live smoke covers the real wire and the push-driven card clear.

const disputedRow: ObligationsRow = {
  obligationId: "11111111-0000-0000-0000-000000000001",
  listingId: "22222222-0000-0000-0000-000000000002",
  disputeId: "33333333-0000-0000-0000-000000000003",
  raisedBy: "44444444-0000-0000-0000-000000000004",
  disputeReason: "NonDelivery",
  resolutionType: null,
  resolutionParticipantId: null,
  winnerId: null,
  sellerId: null,
  escalatedAt: "2026-06-10T14:00:00Z",
  disputeOpenedAt: "2026-06-10T15:00:00Z",
  disputeResolvedAt: null,
  fulfilledAt: null,
  queueState: "Disputed",
};

// A projection gap: an open dispute whose DisputeId never materialized. The control must stay
// hidden — never synthesize an id (M8-S6b prompt open question 3).
const idLessRow: ObligationsRow = {
  ...disputedRow,
  obligationId: "55555555-0000-0000-0000-000000000005",
  listingId: "66666666-0000-0000-0000-000000000006",
  disputeId: null,
};

function renderDisputes(
  rows: ObligationsRow[],
  resolveDispute: ResolveDisputeFn,
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { staleTime: Infinity, retry: false } },
  });
  queryClient.setQueryData(operationsKeys.disputes, rows);
  queryClient.setQueryData<string | null>(
    listingKey(disputedRow.listingId),
    "Boxed vintage synthesizer",
  );
  queryClient.setQueryData<string | null>(
    listingKey(idLessRow.listingId),
    "Hand-thrown ceramic mug",
  );

  render(
    <QueryClientProvider client={queryClient}>
      <StaffAuthProvider>
        <Disputes resolveDispute={resolveDispute} />
      </StaffAuthProvider>
    </QueryClientProvider>,
  );
}

describe("Disputes — the Resolve with extension control (narrative 008 Moment 2)", () => {
  it("renders the control only for rows with a non-null disputeId", () => {
    renderDisputes([disputedRow, idLessRow], vi.fn(async () => {}));

    // One control for the disputed row; the disputeId-less projection gap renders none.
    expect(
      screen.getAllByRole("button", { name: "Resolve with extension" }),
    ).toHaveLength(1);
  });

  it("posts the row's ids through the seam and stays pending after the 202 (the card clearing via push IS the success signal)", async () => {
    const user = userEvent.setup();
    const resolveDispute = vi.fn<ResolveDisputeFn>(async () => {});
    renderDisputes([disputedRow], resolveDispute);

    await user.click(
      screen.getByRole("button", { name: "Resolve with extension" }),
    );

    await waitFor(() =>
      expect(resolveDispute).toHaveBeenCalledWith(expect.any(Function), {
        obligationId: disputedRow.obligationId,
        disputeId: disputedRow.disputeId,
      }),
    );

    // No optimistic cache write and no row removal: the row is still rendered, and its control
    // holds the pending state until the DisputeResolved push re-query drops the card.
    const pending = await screen.findByRole("button", {
      name: "Granting extension…",
    });
    expect(pending).toBeDisabled();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("surfaces a failed resolve visibly on the card and returns the control to actionable", async () => {
    const user = userEvent.setup();
    const resolveDispute = vi.fn<ResolveDisputeFn>(async () => {
      throw new ResolveDisputeError(400, "The dispute was already resolved.");
    });
    renderDisputes([disputedRow], resolveDispute);

    await user.click(
      screen.getByRole("button", { name: "Resolve with extension" }),
    );

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("The dispute was already resolved.");
    // The failure returned the row to actionable — the operator retries deliberately.
    expect(
      screen.getByRole("button", { name: "Resolve with extension" }),
    ).toBeEnabled();
  });
});
