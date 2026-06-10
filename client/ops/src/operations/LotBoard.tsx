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
import { useLotBoard } from "@/operations/queries";
import type { LotBoardRow } from "@/operations/schema";

// Lot board (GET /api/operations/lot-board): the full lifecycle of every tracked listing,
// newest activity first as served. The view's own Title is set-once from ListingPublished and
// nullable until then — the render-time join is the fallback, not the primary, on this board
// (M8-S6 prompt finding 3).

function statusTone(
  status: string,
): "default" | "positive" | "attention" | "destructive" | "muted" {
  switch (status) {
    case "Open":
      return "positive";
    case "Sold":
      return "default";
    case "Draft":
      return "attention";
    case "Passed":
    case "Withdrawn":
      return "muted";
    default:
      return "default";
  }
}

function outcome(row: LotBoardRow): string {
  switch (row.status) {
    case "Sold":
      return row.hammerPrice != null
        ? `${formatMoney(row.hammerPrice)} → ${row.winnerId != null ? shortId(row.winnerId) : "—"}`
        : "—";
    case "Passed":
      return row.passReason ?? "Passed";
    case "Withdrawn":
      return row.withdrawalReason ?? "Withdrawn";
    default:
      return "—";
  }
}

export function LotBoard() {
  const query = useLotBoard();

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-semibold tracking-tight">Lot board</h1>
      <BoardState
        query={query}
        emptyMessage="No listings are on the board yet — the lot board fills as listings publish."
      >
        {(rows) => (
          <BoardTable>
            <BoardTableHead>
              <Th>Listing</Th>
              <Th>Status</Th>
              <Th className="text-right">Current bid</Th>
              <Th className="text-right">Bids</Th>
              <Th>Outcome</Th>
              <Th>Updated</Th>
            </BoardTableHead>
            <BoardTableBody>
              {rows.map((row) => (
                <tr key={row.listingId}>
                  <Td className="font-medium">
                    {row.title ?? <ListingTitle listingId={row.listingId} />}
                  </Td>
                  <Td>
                    <StatusBadge tone={statusTone(row.status)}>
                      {row.status}
                    </StatusBadge>
                  </Td>
                  <Td className="text-right tabular-nums">
                    {formatMoney(row.currentBid ?? row.startingBid)}
                  </Td>
                  <Td className="text-right tabular-nums">{row.bidCount}</Td>
                  <Td className="text-muted-foreground">{outcome(row)}</Td>
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
