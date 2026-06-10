import { useRef, useState } from "react";

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

function timeOf(message: HubMessage): string {
  switch (message.kind) {
    case "listingSold":
      return message.soldAt;
    case "settlementCompleted":
      return message.completedAt;
    default:
      return message.occurredAt;
  }
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

// Bound on the duplicate-suppression set below — enough to cover the redelivery window of a busy
// listing without growing for the page's lifetime.
const MAX_SEEN = 64;

export function LiveActivity({ listingId }: { listingId: string }) {
  const [entries, setEntries] = useState<ActivityEntry[]>([]);
  // Monotonic per-mount sequence for entry keys. The previous id (`${at}-${prev.length}`) produced
  // DUPLICATE React keys once the list was full: prev.length pins at MAX_ENTRIES, and the fan-out's
  // per-queue copies of one event share the same occurredAt. Duplicate keys corrupt list
  // reconciliation — React leaves ghost <li> nodes behind, and the feed visibly grows far past the
  // cap (observed: 33 rendered entries, 65 duplicate-key warnings after a 6-bid mash).
  const seqRef = useRef(0);
  // The modular-monolith topology delivers each integration event once per consuming RabbitMQ
  // queue (at-least-once × fan-out), so the same push arrives several times. The feed is the one
  // surface with no idempotent write to absorb that — dedupe by message identity here.
  const seenRef = useRef(new Set<string>());

  useListen((message) => {
    if (message.listingId !== listingId) return;
    const text = describe(message);
    if (text === null) return;
    const at = timeOf(message);

    const identity =
      message.kind === "bidPlaced" ? `bid-${message.bidId}` : `${message.kind}-${at}-${text}`;
    if (seenRef.current.has(identity)) return;
    seenRef.current.add(identity);
    if (seenRef.current.size > MAX_SEEN) {
      const oldest = seenRef.current.values().next().value;
      if (oldest !== undefined) seenRef.current.delete(oldest);
    }

    const id = `${seqRef.current++}`;
    setEntries((prev) => [{ id, text, at }, ...prev].slice(0, MAX_ENTRIES));
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
