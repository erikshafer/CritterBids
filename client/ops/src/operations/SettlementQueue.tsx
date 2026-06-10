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
import { formatMoney, formatTimestamp, shortId } from "@/operations/format";
import { useSettlementQueue } from "@/operations/queries";

// Settlement queue (GET /api/operations/settlement-queue): every settlement row, newest activity
// first, with Failed rows flagged (the PaymentFailed case the milestone names). This board has
// ZERO ops-feed push coverage (M8-S6 prompt finding 2) — its query polls modestly until the
// Relay ops-feed carry-forward lands; see queries.ts.

function statusTone(
  status: string,
): "default" | "positive" | "attention" | "destructive" | "muted" {
  switch (status) {
    case "Failed":
      return "destructive";
    case "Completed":
      return "default";
    case "PaidOut":
      return "positive";
    default:
      return "default";
  }
}

export function SettlementQueue() {
  const query = useSettlementQueue();

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-semibold tracking-tight">
        Settlement queue
      </h1>
      <BoardState
        query={query}
        emptyMessage="No settlements in flight — rows appear when a gavel falls."
      >
        {(rows) => (
          <BoardTable>
            <BoardTableHead>
              <Th>Listing</Th>
              <Th>Winner</Th>
              <Th className="text-right">Hammer</Th>
              <Th className="text-right">Fee</Th>
              <Th className="text-right">Payout</Th>
              <Th>Status</Th>
              <Th>Updated</Th>
            </BoardTableHead>
            <BoardTableBody>
              {rows.map((row) => (
                <tr key={row.settlementId}>
                  <Td className="font-medium">
                    <ListingTitle listingId={row.listingId} />
                  </Td>
                  <Td className="font-mono" title={row.winnerId}>
                    {shortId(row.winnerId)}
                  </Td>
                  <Td className="text-right tabular-nums">
                    {formatMoney(row.hammerPrice)}
                  </Td>
                  <Td className="text-right tabular-nums">
                    {formatMoney(row.feeAmount ?? row.feeDeducted)}
                  </Td>
                  <Td className="text-right tabular-nums">
                    {formatMoney(row.sellerPayout ?? row.payoutAmount)}
                  </Td>
                  <Td>
                    <StatusBadge tone={statusTone(row.status)}>
                      {row.status}
                    </StatusBadge>
                    {row.status === "Failed" && row.failureReason != null ? (
                      <p className="text-destructive mt-1 text-sm">
                        {row.failureReason}
                      </p>
                    ) : null}
                  </Td>
                  <Td className="text-muted-foreground tabular-nums">
                    {formatTimestamp(row.lastUpdatedAt)}
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
