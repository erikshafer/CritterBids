import { useRef, useState } from "react";

import { useListen } from "@/signalr/hooks";
import type { HubMessage } from "@/signalr/messages";
import { formatUsd } from "@/lib/format";

interface ActivityEntry {
  id: string;
  text: string;
  at: string;
}

const MAX_ENTRIES = 8;
const MAX_SEEN = 64;

function timeOf(message: HubMessage): string {
  switch (message.kind) {
    case "listingSold":
      return message.soldAt;
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
      return message.payload;
    default:
      return null;
  }
}

export function LiveActivity({ listingId }: { listingId: string }) {
  const [entries, setEntries] = useState<ActivityEntry[]>([]);
  const seqRef = useRef(0);
  const seenRef = useRef(new Set<string>());

  useListen((message) => {
    if (message.listingId !== listingId) return;
    const text = describe(message);
    if (text === null) return;
    const at = timeOf(message);

    const identity =
      message.kind === "bidPlaced"
        ? `bid-${message.bidId}`
        : `${message.kind}-${at}-${text}`;
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
