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

// The two OperationsObligationsView queues (narrative 008 — Morgan's working surfaces):
//
//   Escalations (GET /api/operations/obligations/escalations): obligations whose ship-by
//   deadline lapsed without recovery (QueueState == Escalated), newest escalation first.
//   Arrivals are NOT live-pushed (no DeadlineEscalated on the ops feed — prompt finding 2);
//   departures are (DisputeOpened moves a card to the dispute queue). The query polls modestly
//   as the documented stopgap.
//
//   Disputes (GET /api/operations/obligations/disputes): obligations with an open dispute
//   (QueueState == Disputed), newest first. The card carries what narrative 008 Moment 1 names:
//   the listing, who raised it, the reason, and the obligation's escalation history. Arrivals
//   and resolutions are live via DisputeOpened/DisputeResolved.
//
// Read-only queues: resolving a dispute (Moment 2's ResolveDispute) is unscoped M8 work — no
// command surface here.

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

export function Disputes() {
  const query = useDisputes();

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
            </BoardTableHead>
            <BoardTableBody>
              {rows.map((row) => (
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
                </tr>
              ))}
            </BoardTableBody>
          </BoardTable>
        )}
      </BoardState>
    </div>
  );
}
