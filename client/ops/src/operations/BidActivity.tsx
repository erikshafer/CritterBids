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
import { useBidActivity } from "@/operations/queries";

// Bid activity (GET /api/operations/bid-activity): the append-style feed of every accepted bid,
// newest first as served. Rows are keyed by BidId — the notification-identity rule (the at-least-
// once topology can duplicate pushes; the re-queried board is naturally deduplicated, and BidId
// keys keep React reconciliation honest either way).

export function BidActivity() {
  const query = useBidActivity();

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-semibold tracking-tight">Bid activity</h1>
      <BoardState
        query={query}
        emptyMessage="No bids yet — the feed fills the moment bidding opens."
      >
        {(rows) => (
          <BoardTable>
            <BoardTableHead>
              <Th>Placed</Th>
              <Th>Listing</Th>
              <Th>Bidder</Th>
              <Th className="text-right">Amount</Th>
              <Th className="text-right">Bid #</Th>
              <Th>Origin</Th>
            </BoardTableHead>
            <BoardTableBody>
              {rows.map((row) => (
                <tr key={row.bidId}>
                  <Td className="text-muted-foreground tabular-nums">
                    {formatTimestamp(row.placedAt)}
                  </Td>
                  <Td className="font-medium">
                    <ListingTitle listingId={row.listingId} />
                  </Td>
                  <Td className="font-mono" title={row.bidderId}>
                    {shortId(row.bidderId)}
                  </Td>
                  <Td className="text-right font-medium tabular-nums">
                    {formatMoney(row.amount)}
                  </Td>
                  <Td className="text-right tabular-nums">{row.bidCount}</Td>
                  <Td>
                    {row.isProxy ? (
                      <StatusBadge tone="attention">Proxy</StatusBadge>
                    ) : (
                      <span className="text-muted-foreground">Direct</span>
                    )}
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
