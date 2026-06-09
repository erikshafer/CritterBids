import { useState } from "react";

import { useListen } from "@/signalr/hooks";
import type { HubMessage } from "@/signalr/messages";
import { formatUsd } from "@/lib/format";

// Narrative 001 Moment 5 — the live bid feed (others' bids appearing). This is the `useListen` half
// of ADR 026: a TRANSIENT activity log fed straight from hub pushes. It is deliberately NOT the
// source of the headline current bid (that comes from the re-queried view) — a ticker of "what just
// happened," capped so it can't grow without bound. Pushes for OTHER listings are ignored.

interface ActivityEntry {
  id: string;
  text: string;
  at: string;
}

const MAX_ENTRIES = 8;

// The wire uses different timestamp field names per shape (occurredAt vs soldAt).
function timeOf(message: HubMessage): string {
  return message.kind === "listingSold" ? message.soldAt : message.occurredAt;
}

function describe(message: HubMessage): string | null {
  switch (message.kind) {
    case "bidPlaced":
      return `New bid ${formatUsd(message.amount)}`;
    case "listingSold":
      return `Sold for ${formatUsd(message.hammerPrice)}`;
    case "listingEvent":
      // The human-readable payload string the server composed for these eventTypes (ReserveMet,
      // ExtendedBiddingTriggered, BiddingOpened, …). A transient label, not a parsed authority.
      return message.payload;
    case "bidderEvent":
      return message.payload;
    default:
      return null;
  }
}

export function LiveActivity({ listingId }: { listingId: string }) {
  const [entries, setEntries] = useState<ActivityEntry[]>([]);

  useListen((message) => {
    if (message.listingId !== listingId) return;
    const text = describe(message);
    if (text === null) return;
    const at = timeOf(message);
    setEntries((prev) =>
      [{ id: `${at}-${prev.length}`, text, at }, ...prev].slice(0, MAX_ENTRIES),
    );
  });

  if (entries.length === 0) {
    return (
      <p className="text-muted-foreground text-xs">
        Waiting for live activity…
      </p>
    );
  }

  return (
    <ul className="space-y-1" aria-label="Live activity">
      {entries.map((entry) => (
        <li key={entry.id} className="text-muted-foreground text-xs">
          {entry.text}
        </li>
      ))}
    </ul>
  );
}
