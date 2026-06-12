import { useState } from "react";
import { useMutation } from "@tanstack/react-query";

import { useStaffAuth } from "@/auth/StaffAuthContext";
import { Button } from "@/components/ui/button";
import { BoardState } from "@/operations/BoardState";
import { ListingTitle } from "@/operations/ListingTitle";
import {
  BoardTable,
  BoardTableBody,
  BoardTableHead,
  StatusBadge,
  Td,
  Th,
} from "@/operations/Table";
import { formatTimestamp, shortId } from "@/operations/format";
import { useDisputes, useEscalations } from "@/operations/queries";
import {
  resolveDisputeWithExtension,
  type ResolveDisputeFn,
  type ResolveDisputeInput,
} from "@/operations/resolveDispute";

// The two OperationsObligationsView queues (narrative 008 — Morgan's working surfaces):
//
//   Escalations (GET /api/operations/obligations/escalations): obligations whose ship-by
//   deadline lapsed without recovery (QueueState == Escalated), newest escalation first.
//   Arrivals and departures are both live since M8-S6b: DeadlineEscalated joined the ops feed
//   (completing the gap the M8-S6 polling stopgap covered), and DisputeOpened moves a card to
//   the dispute queue.
//
//   Disputes (GET /api/operations/obligations/disputes): obligations with an open dispute
//   (QueueState == Disputed), newest first. The card carries what narrative 008 Moment 1 names:
//   the listing, who raised it, the reason, and the obligation's escalation history — plus,
//   since M8-S6b, Moment 2's action: "Resolve with extension" (the one non-terminal resolution;
//   Refund/Closed stay un-surfaced). Arrivals and resolutions are live via
//   DisputeOpened/DisputeResolved.

export function Escalations() {
  const query = useEscalations();

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-semibold tracking-tight">Escalations</h1>
      <BoardState
        query={query}
        emptyMessage="The escalation queue is clear — no obligation has lapsed its ship-by deadline."
      >
        {(rows) => (
          <BoardTable>
            <BoardTableHead>
              <Th>Listing</Th>
              <Th>Escalated</Th>
              <Th>Winner</Th>
              <Th>Seller</Th>
              <Th>State</Th>
            </BoardTableHead>
            <BoardTableBody>
              {rows.map((row) => (
                <tr key={row.obligationId}>
                  <Td className="font-medium">
                    <ListingTitle listingId={row.listingId} />
                  </Td>
                  <Td className="text-muted-foreground tabular-nums">
                    {formatTimestamp(row.escalatedAt)}
                  </Td>
                  <Td className="font-mono">
                    {row.winnerId != null ? shortId(row.winnerId) : "—"}
                  </Td>
                  <Td className="font-mono">
                    {row.sellerId != null ? shortId(row.sellerId) : "—"}
                  </Td>
                  <Td>
                    <StatusBadge tone="attention">{row.queueState}</StatusBadge>
                  </Td>
                </tr>
              ))}
            </BoardTableBody>
          </BoardTable>
        )}
      </BoardState>
    </div>
  );
}

export interface DisputesProps {
  /** Test seam: inject a fake resolve call. Production posts the real StaffOnly endpoint. */
  resolveDispute?: ResolveDisputeFn;
}

export function Disputes({
  resolveDispute = resolveDisputeWithExtension,
}: DisputesProps) {
  const query = useDisputes();
  const { staffFetch } = useStaffAuth();

  // Per-row action state, keyed by obligationId. A row STAYS pending after a 202: the saga
  // resolves asynchronously and the DisputeResolved push re-queries both queues — the card
  // leaving the data is the success signal (ADR 026; no optimistic cache write, no manual row
  // removal). Only a failure returns the row to actionable, with the error visible on the card.
  const [pendingIds, setPendingIds] = useState<ReadonlySet<string>>(
    () => new Set(),
  );
  const [rowErrors, setRowErrors] = useState<ReadonlyMap<string, string>>(
    () => new Map(),
  );

  const mutation = useMutation({
    // retry stays at the mutation default (none): the saga applies the resolution after the
    // 202, so a lost response is indistinguishable from success — the operator re-clicks
    // deliberately if the card never clears (wolverine-http-frontend-contract §6).
    mutationFn: (input: ResolveDisputeInput) => resolveDispute(staffFetch, input),
  });

  function requestExtension(obligationId: string, disputeId: string) {
    setRowErrors((previous) => {
      const next = new Map(previous);
      next.delete(obligationId);
      return next;
    });
    setPendingIds((previous) => new Set(previous).add(obligationId));
    mutation.mutate(
      { obligationId, disputeId },
      {
        onError: (error) => {
          // A 401 has additionally funnelled through staffFetch into clear-token + re-gate;
          // for everything else the card surfaces the failure — never silently.
          setPendingIds((previous) => {
            const next = new Set(previous);
            next.delete(obligationId);
            return next;
          });
          setRowErrors((previous) =>
            new Map(previous).set(
              obligationId,
              error instanceof Error ? error.message : String(error),
            ),
          );
        },
      },
    );
  }

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-semibold tracking-tight">Disputes</h1>
      <BoardState
        query={query}
        emptyMessage="No disputes are open — the post-sale floor is healthy."
      >
        {(rows) => (
          <BoardTable>
            <BoardTableHead>
              <Th>Listing</Th>
              <Th>Reason</Th>
              <Th>Raised by</Th>
              <Th>Opened</Th>
              <Th>History</Th>
              <Th>Action</Th>
            </BoardTableHead>
            <BoardTableBody>
              {rows.map((row) => {
                const disputeId = row.disputeId;
                const isPending = pendingIds.has(row.obligationId);
                const rowError = rowErrors.get(row.obligationId);
                return (
                  <tr key={row.obligationId}>
                    <Td className="font-medium">
                      <ListingTitle listingId={row.listingId} />
                    </Td>
                    <Td>
                      <StatusBadge tone="destructive">
                        {row.disputeReason ?? "Unspecified"}
                      </StatusBadge>
                    </Td>
                    <Td className="font-mono">
                      {row.raisedBy != null ? shortId(row.raisedBy) : "—"}
                    </Td>
                    <Td className="text-muted-foreground tabular-nums">
                      {formatTimestamp(row.disputeOpenedAt)}
                    </Td>
                    <Td className="text-muted-foreground">
                      {row.escalatedAt != null
                        ? `Escalated ${formatTimestamp(row.escalatedAt)}; no tracking on file`
                        : "—"}
                    </Td>
                    <Td>
                      {disputeId != null ? (
                        <div className="space-y-1">
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={isPending}
                            onClick={() =>
                              requestExtension(row.obligationId, disputeId)
                            }
                          >
                            {isPending
                              ? "Granting extension…"
                              : "Resolve with extension"}
                          </Button>
                          {rowError != null && (
                            <p role="alert" className="text-sm text-red-400">
                              {rowError}
                            </p>
                          )}
                        </div>
                      ) : (
                        // A disputeId-less row is a projection gap: render no control,
                        // never synthesize an id (M8-S6b prompt open question 3).
                        "—"
                      )}
                    </Td>
                  </tr>
                );
              })}
            </BoardTableBody>
          </BoardTable>
        )}
      </BoardState>
    </div>
  );
}
